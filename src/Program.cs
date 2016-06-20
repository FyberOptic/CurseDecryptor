using System;
using System.Threading;
using System.Reflection;
using System.IO;


/**
 * CurseDecryptor v1.0
 * -------------------
 * 
 * This tool is designed to extract the Client.dat and Overlay.dat files in
 * your CurseVoice client's "Bin\Assets" folder.  These files are both
 * compressed and encrypted.  Luckily, Curse's own code can take care
 * of all the heavy lifting, while simultaneously avoiding the legal issues 
 * of including decryption keys or anything in this decompressor.
 * 
 * The filenames of the assets contained in these archives no longer exist.
 * It only stores filename hashes.  The client hashes any requested filename
 * before attempting to retrieve it from the archive.  When extracting all
 * files with this tool, you might want to also use a third-party tool such 
 * as TrID to automatically detect file types and rename them.  You'll
 * likely still end up renaming some of them by hand, however.
 * 
 * 
 * Usage:
 *   CurseDecryptor -all <archive> <output_dir>
 *   CurseDecryptor -single <archive> <asset_filename> <output_dir>
 *   
 * 
 * NOTE: The output directory should already exist.
 *    
 *   
 * Modes:
 *   -all: 
 *     Extracts all files into the output directory.  Files will be named 
 *     "file######.dat", where ###### is the hash stored in the archive.
 *     
 *         
 *   -single: 
 *     Extracts a single file to the output directory.  The path provided 
 *     should be the original name, not the hash.  Any subdirectories in
 *     the requested file will be automatically created in the output
 *     directory.
 * 
 * 
 * 
 * Dependencies:
 *    The following DLLs from your CurseVoice installation are required:
 *    - Curse.Logging.dll
 *    - Curse.Radium.Html.dll
 *    - LzmaLib.dll
 *    - Newtonsoft.Json.dll
 *  
 * 
 * 
 * 
 * 
 * License:
 *   Licensed under the Apache License, Version 2.0 (the "License");
 *   you may not use this file except in compliance with the License.
 *   You may obtain a copy of the License at
 *   
 *     http://www.apache.org/licenses/LICENSE-2.0
 *   
 *   Unless required by applicable law or agreed to in writing, software
 *   distributed under the License is distributed on an "AS IS" BASIS,
 *   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *   See the License for the specific language governing permissions and
 *   limitations under the License.
 *
 */




namespace CurseDecryptor
{
    class Program
    {
        static string version = "1.0";


        static int doExit(int code)
        {
            if (System.Diagnostics.Debugger.IsAttached)
            {
                Console.Write("\nPress any key to continue...");
                Console.ReadKey();
                Console.WriteLine("");
            }
            return code;
        }


        static int Main(string[] args)
        {
            bool badSyntax = false;
            bool extractAll = false;
            bool extractSingle = false;

            if (args.Length < 1) badSyntax = true;
            else
            {
                string cmd = args[0].ToLower();

                if (cmd.Equals("-all")) extractAll = true;
                if (cmd.Equals("-single")) extractSingle = true;

                if (!extractAll && !extractSingle) badSyntax = true;

                if (extractAll && args.Length != 3) badSyntax = true;
                if (extractSingle && args.Length != 4) badSyntax = true;
            }

            Console.WriteLine(" --- CurseDecryptor v" + version + " ---\n");

            if (badSyntax)
            {               
                Console.WriteLine("Syntax:");
                Console.WriteLine("  CurseDecryptor -all <archive> <output_dir>");
                Console.WriteLine("  CurseDecryptor -single <archive> <asset_filename> <output_dir>");
                return doExit(1);
            }

            string inputArchive = args[1];
            string singleFilename = extractSingle ? args[2] : null;
            string outputDirName = args[extractAll ? 2 : 3];
            if (outputDirName.Length < 1) outputDirName = ".";

            if (!File.Exists(inputArchive))
            {
                Console.WriteLine("Error: Specified archive doesn't exist - " + inputArchive);
                return doExit(2);
            }

            if (!Directory.Exists(outputDirName))
            {
                Console.WriteLine("Error: Specified output directory doesn't exist - " + outputDirName);
                return doExit(3);
            }



            // Set up Curse's logger to the current directory, to avoid a crash.
            Curse.Logging.Logger.Init(".", "CurseDecryptor");


            // Load the asset package.
            Console.WriteLine("Using archive " + inputArchive);
            Curse.Radium.Html.AssetPackage ap = new Curse.Radium.Html.AssetPackage(inputArchive);

            if (ap == null)
            {
                Console.WriteLine("Error: Couldn't create AssetPackage instance!");
                return doExit(5);
            }


            // We'll need reflection since some required members are private.
            Type apType = typeof(Curse.Radium.Html.AssetPackage);
            FieldInfo inProgressField = apType.GetField("decompressionInProgress", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo getNoncompressedMethod = apType.GetMethod("GetFileDataNoncompressed", BindingFlags.NonPublic | BindingFlags.Instance);


            // A thread is spawned by AssetPackage's initialization to decompress any
            // compressed files in the archive, so we need to wait till it's done.
            // Keep in mind that not all files in the archive are compressed.
            Console.Write("Sleeping during AssetPackage decompression thread...");
            while (true)
            {
                bool inProgress = (bool)inProgressField.GetValue(ap);
                if (inProgress) Thread.Sleep(1);
                else break;
            }
            Console.WriteLine("done\n");


            if (extractAll)
            {
                // Dump the contents of the compressed files to disk.            
                Console.Write("Extracting compressed files...");
                int compressedCount = 0;
                foreach (uint key in ap.CompressedFileData.Keys)
                {
                    // File contents were cached during decompression, so we can grab it directly.
                    byte[] data = ap.CompressedFileData[key];
                    FileStream outFile = File.Open(Path.Combine(outputDirName, "file" + key + ".dat"), FileMode.Create);
                    outFile.Write(data, 0, data.Length);
                    outFile.Close();
                    compressedCount++;
                }
                Console.WriteLine("done (" + compressedCount + ")");


                // Extract contents of the remaining noncompressed files to disk.
                Console.Write("Extracting noncompressed files...");
                int noncompressedCount = 0;
                foreach (uint key in ap.NoncompressedFileData.Keys)
                {
                    // These aren't cached, we have to retrieve the data from the archive.
                    byte[] data = (byte[])getNoncompressedMethod.Invoke(ap, new object[] { key });
                    FileStream outFile = File.Open(Path.Combine(outputDirName, "file" + key + ".dat"), FileMode.Create);
                    outFile.Write(data, 0, data.Length);
                    outFile.Close();
                    noncompressedCount++;
                }
                Console.WriteLine("done (" + noncompressedCount + ")");

                Console.WriteLine("\nTotal files extracted: " + (noncompressedCount + compressedCount));
            }


            else if (extractSingle)
            {
                byte[] data = ap.GetFileData(singleFilename);
                if (data == null)
                {
                    Console.WriteLine("Error: Couldn't find \"" + singleFilename + "\" in archive!");                    
                    return doExit(4);
                }

                // Strip leading slashes to prevent Path.Combine from creating absolute path.
                string outputFile = Path.Combine(outputDirName, singleFilename.TrimStart(new char[] { '\\', '/' }));

                // Create potential output subdirectories.
                string outputPath = Path.GetDirectoryName(outputFile);
                if (!Directory.Exists(outputPath))
                    Directory.CreateDirectory(outputPath);               

                FileStream outFile = File.Open(outputFile, FileMode.Create);
                outFile.Write(data, 0, data.Length);
                outFile.Close();

                Console.WriteLine("Single file extracted: " + outputFile);
            }

            

            return doExit(0);
        }
    }
}
