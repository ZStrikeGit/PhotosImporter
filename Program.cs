using ManyConsole;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System.Data.Odbc;
using NExifTool;
using Nito.AsyncEx.Synchronous;
using Nito.AsyncEx;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.Management.Automation;
using Microsoft.PowerShell;
using System.IO;
using Microsoft.PowerShell.Commands;
namespace PhotosImporter
{
    public class Program
    {
        public static int Main(string[] args)
        {
            var commands = GetCommands();

            return ConsoleCommandDispatcher.DispatchCommand(commands, args, Console.Out);
        }

        public static IEnumerable<ConsoleCommand> GetCommands()
        {
            return ConsoleCommandDispatcher.FindCommandsInSameAssemblyAs(typeof(Program));
        }
    }
    public class ImportCommand : ConsoleCommand
    {
        private const int Success = 0;
        private const int Failure = 2;

        public string PhotosPath { get; set; }
        public string PhotosTable { get; set; }

        private static bool HasNonASCIIChars(string str)
        {
            return (System.Text.Encoding.UTF8.GetByteCount(str) != str.Length);
        }

        private static string[] GetBannedExtensions(string path)
        {
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<string[]>(json);
        }

        private static string[] GetVideoExtensions(string path)
        {
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<string[]>(json);
        }

        private static string Escape(string input)
        {
            return input.Replace("\"", "\\\"").Replace("'", "\\'");
        }
        private static bool IsBadFilename(string filename)
        {
            bool prq1 = filename.Contains("'") || filename.Contains('"') || filename.Contains(',') || filename.Contains('`') || filename.Contains('’') || filename.Contains('—');
            bool prq2 = HasNonASCIIChars(filename);

            return (prq1 || prq2);
        }

        private static void FixBadFilenameAndUpdateRecord(FileInfo file, string[] videoExtensions, string table)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            //invalidChars.Append('\'');
            string newFileName = string.Join("_", Encoding.ASCII.GetString(ASCIIEncoding.Convert(Encoding.Unicode, Encoding.ASCII, Encoding.Unicode.GetBytes(file.Name))).Split(invalidChars));
            string newPath = Path.Combine(file.Directory.FullName,newFileName);
            //string newFilename = filename.Replace("'", "");//.Replace('"', '').Replace(",", "").Replace("`", "").Replace("’", "").Replace("—", "-");
            //File.Move(table + filename, table + newFilename);
            file.MoveTo(newPath);
            UpdateRecord(new FileInfo(newPath), videoExtensions, table);
            
        }

        private static bool IsBannedExtension(string filename, string[] bannedExtensions)
        {
            string extension = Path.GetExtension(filename).ToLower();
            return bannedExtensions.Contains(extension);
        }

        private static void UpdateRecord(FileInfo file, string[] videoExtensions, string table)
        {
            string name = Path.GetFileNameWithoutExtension(file.FullName);
            string hash = GetFileHash(file);
            string extension = file.Extension;
            string tags = GetFileTags(file);
            int isVideo = 0;
            if (videoExtensions.Contains(extension))
            {
                isVideo = 1;
            }
            double fileSizeMb = GetFileSizeMB(file);

            using (OdbcConnection connection = new OdbcConnection("DSN=photos"))
            {
                connection.Open();
                //string query = $"INSERT INTO {table} (name, hash, ext, tags, video, size) VALUES (@name, @hash, @ext, @tags, @video, @size)";
                string query = $"INSERT INTO {table} (name, hash, ext, tags, video, size) VALUES (\"{name}\", \"{Escape(hash)}\", \"{extension.Substring(1)}\", \'{tags}\', {isVideo.ToString()}, {fileSizeMb.ToString()})";
                //string query = $"INSERT INTO {table} (name, hash, ext, tags, video, size) VALUES (?, ?, ?, ?, ?, ?)";
                //Console.WriteLine(query);
                OdbcCommand command = new(query, connection);

                //command.Parameters.AddWithValue("@name", name);
                //command.Parameters.AddWithValue("@hash", hash);
                //command.Parameters.AddWithValue("@ext", extension);
                //command.Parameters.AddWithValue("@tags", tags);
                //command.Parameters.AddWithValue("@video", isVideo);
                //command.Parameters.AddWithValue("@size", fileSizeMb);
                
                Console.WriteLine(command.CommandText);
                command.ExecuteNonQuery();
            }
        }

        private static string GetFileHash(FileInfo file)
        {
            string res = "";
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                if (file.Length < (500 * 1024 * 1024))
                {
                    using (var stream = File.OpenRead(file.FullName))
                    {
                        var hash = md5.ComputeHash(stream);
                        res = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                } else
                {
                    PowerShell ps = PowerShell.Create();
                    ps.AddCommand("Get-FileHash").AddParameter("Path", file.FullName).AddParameter("Algorithm", "MD5");
                    dynamic results = ps.Invoke();
                    foreach (var item in results)
                    {
                        //Type t = item.GetType();
                        //if (t is FileHashInfo)
                        //{
                        res = item.Hash.ToString().ToLowerInvariant();
                            
                    }
                }
                return res;
            }
        }

        private static string GetFileTags(FileInfo file)
        {
            // Start the child process.
            Process p = new();
            // Redirect the output stream of the child process.
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = "C:\\Users\\ZStrike\\.aftman\\bin\\exiftool.exe";
            p.StartInfo.Arguments = $"\"{(file.FullName)}\" -TagsList -json";
            // FileName = "C:\\Deps\\getopt.bat";
            p.Start();
            // Do not wait for the child process to exit before
            // reading to the end of its redirected stream.
            // p.WaitForExit();
            // Read the output stream first and then wait.
            string output = p.StandardOutput.ReadToEnd();
            Console.WriteLine(output);
            dynamic tmp = JsonConvert.DeserializeObject(output);
            
            p.WaitForExit();
            if (tmp[0].ContainsKey("TagsList")) {
               return JsonConvert.SerializeObject(tmp[0]["TagsList"]);
            } else
            {
                return "{}";
            }

            //Console.WriteLine("Line 100: Tags (none)");
            // This method is not implemented as it requires a third-party library to read EXIF tags.
            // You can use a library like ExifLib or MetadataExtractor.
            //ExifToolOptions opts = new ExifToolOptions { ExifToolPath = "C:\\Users\\ZStrike\\.aftman\\bin\\exiftool.exe" };

            //var tagresult = new ExifTool(opts).GetTagsAsync(filePath).WaitAndUnwrapException();
            //Console.WriteLine(tagresult);

        }

        private static double GetFileSizeMB(FileInfo file)
        {

            return (double)file.Length / (1024 * 1024);
        }

        private static void WriteBadFileNotifications(List<string> badFileNotifications)
        {
            using (StreamWriter writer = new StreamWriter("invalidfiles.txt"))
            {
                foreach (string notification in badFileNotifications)
                {
                    writer.WriteLine(notification);
                }
            }
        }

        private static void DeleteRecords(string table)
        {
            using (OdbcConnection connection = new OdbcConnection("DSN=photos"))
            {
                connection.Open();

                string query = $"DELETE FROM {table}";
                OdbcCommand command = new OdbcCommand(query, connection);

                command.ExecuteNonQuery();
                Console.WriteLine("Deleted!");
            }
        }

        private static void ReIndexRecords(string table)
        {
            using (OdbcConnection connection = new OdbcConnection("DSN=photos"))
            {
                connection.Open();

                string query = $"ALTER TABLE {table} AUTO_INCREMENT = 1;";
                OdbcCommand command = new OdbcCommand(query, connection);

                command.ExecuteNonQuery();
            }
        }

        public ImportCommand() {
            // Register the actual command with a simple (optional) description.
            IsCommand("Import", "Import Files");

            // Add a longer description for the help on that specific command.
            HasLongDescription("This can be used to quickly read a file's contents " +
            "while optionally stripping out the ',' character.");

            // Required options/flags, append '=' to obtain the required value.
            HasRequiredOption("p|path=", "Path", p => PhotosPath = p);
            HasRequiredOption("t|table=", "Table", t => PhotosTable = t);

            // Optional options/flags, append ':' to obtain an optional value, or null if not specified.
            //HasOption("s|strip:", "Strips ',' from the file before writing to output.",
            //    t => StripCommaCharacter = t == null ? true : Convert.ToBoolean(t));
        }

        public override int Run(string[] remainingArguments)
        {
            string sqlFilePath = Path.Combine(System.Reflection.Assembly.GetExecutingAssembly().Location, "output.sql");
            System.IO.FileStream sqlFile = File.OpenWrite(sqlFilePath); // base class not allowed???

            
                string bannedExtensionsPath = "C:/Deps/bannedExtensions.json";
                string videoExtensionsPath = "C:/Deps/videoExtensions.json";

                string[] bannedExtensions = GetBannedExtensions(bannedExtensionsPath);
                string[] videoExtensions = GetVideoExtensions(videoExtensionsPath);

                List<string> badFileNotifications = new List<string>();
                Console.WriteLine("Deleting and re-indexing in 10 seconds...");
               // System.Threading.Thread.Sleep(10000);

                DeleteRecords(PhotosTable);
                ReIndexRecords(PhotosTable);
                foreach (string filename in Directory.GetFiles(PhotosPath))
                {
                    FileInfo file = new(filename);
                if (!file.Name.StartsWith('.'))
                {
                    Console.WriteLine($"{file.Name}");
                    //Path.GetFileName(filename);

                    if (IsBadFilename(file.Name) && !IsBannedExtension(file.Name, bannedExtensions))
                    {
                        badFileNotifications.Add($"Bad Filename: {file}");
                        Console.WriteLine($"Bad Filename: {file.Name}");
                        FixBadFilenameAndUpdateRecord(file, videoExtensions, PhotosTable);
                    }

                    if (!IsBannedExtension(file.Name, bannedExtensions))
                    {
                        UpdateRecord(file, videoExtensions, PhotosTable);
                    }
                    Console.WriteLine("Processed: " + file.Name);
                }

                WriteBadFileNotifications(badFileNotifications);
                }
            foreach (var item in badFileNotifications)
            {
                Console.WriteLine($"{item}");
            }


            return Success;

        }
        
    }
}