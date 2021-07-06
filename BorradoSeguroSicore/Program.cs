using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace BorradoSeguroSicore
{
    class Program
    {
        static readonly String ERASER_ARGUMENTS = "erase /method=b1bfab4a-31d3-43a5-914c-e9892c78afd8 /target file=";
        static readonly DateTime NOW = DateTime.Now;
        static String WORKSPACE = @"E:\Test";
        //static readonly String LOG_FILE = @"log.txt";
        static String ERASER_PATH = @"C:\Program Files\Eraser\Eraser.exe";
        static string PARAMETERS_FILE;
        static int EXCEPTIONAL_TIME = 60;
        static int NORMAL_TIME = 10;

        static void Main(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    System.Console.WriteLine("Ingrese la ruta del archivo de parámetros");
                    return;
                }

                PARAMETERS_FILE = args[0];

                Log("Leyendo parámetros", EventLogEntryType.Information);
                readParameters();
                Log("Parámetros leídos", EventLogEntryType.Information);

                if (!EraserExists())
                {
                    String msg = "No se ha encontrado una instalación de Eraser con la ruta configurada " + ERASER_PATH + ". contacte al Administrador.";
                    Log(msg, EventLogEntryType.Error);
                    throw new System.ApplicationException(msg);
                }

                int cont = 0;

                Erase(WORKSPACE);

                Delete(WORKSPACE);

                Log("Archivos eliminados: " + cont + ". Programa terminado", EventLogEntryType.Information);
            }
            catch (Exception e)
            {
                Log(e.Message, EventLogEntryType.Error);
                throw e;
            }

            System.Environment.Exit(1);
        }

        static void readParameters()
        {
            try
            {
                FileStream fileStream = new FileStream(PARAMETERS_FILE, FileMode.Open);

                using (StreamReader reader = new StreamReader(fileStream))
                {
                    while (reader.Peek() >= 0)
                    {
                        string line = reader.ReadLine();

                        string[] tokens = line.Split('=');

                        if (tokens.Length != 2)
                        {
                            String msg = "Formato no válido. Los parámetros deben ser especificados en la forma: [NOMBRE] = [VALOR]";
                            Log(msg, EventLogEntryType.Error);
                            throw new System.ApplicationException(msg);
                        }

                        switch (tokens[0])
                        {
                            case "ERASER_HOME":
                                ERASER_PATH = tokens[1] + @"Eraser.exe";
                                break;
                            case "PATHS":
                                WORKSPACE = tokens[1];
                                break;
                            default:
                                String msg = "Parámetro no válido. Valores aceptados: ERASER_HOME, PATHS, RETENTION";
                                Log(msg, EventLogEntryType.Error);
                                throw new System.ApplicationException(msg);
                        }

                    }
                }
            }
            catch (FileNotFoundException e)
            {
                String msg = "El archivo de parámetros 'parameters.txt' no existe. Debe crear este archivo en la ruta donde se encuentra el ejecutable del aplicativo.";
                Log(msg, EventLogEntryType.Error);
                throw new System.ApplicationException(msg);
            }
            catch (FormatException e2)
            {
                String msg = "Formato no válido. Los parámetros deben ser especificados en la forma: [NOMBRE] = [VALOR]";
                Log(msg, EventLogEntryType.Error);
                throw new System.ApplicationException(msg);
            }
        }


        static Boolean EraserExists()
        {
            return File.Exists(ERASER_PATH);
        }

        static void Erase(String path)
        {

            foreach (String file in Directory.GetFiles(path))
            {
                CallEraserOnFile(file);
            }

            foreach (String directory in Directory.GetDirectories(path))
            {
                Erase(directory);
            }
        }

        static void Delete(String path)
        {
            try
            {
                if ((Directory.GetFiles(path).Length + Directory.GetDirectories(path).Length) == 0 && path != WORKSPACE)
                {
                    Directory.Delete(path, true);
                }

                foreach (String directory in Directory.GetDirectories(path))
                {
                    Delete(directory);
                }
            }
            catch (DirectoryNotFoundException e)
            {
                Log(e.Message, EventLogEntryType.Warning);
            }
        }

        static void CallEraserOnFile(String path)
        {
            try
            {
                // Condición de borrado
                if (!IsElegibleForErasure(path))
                {
                    return;
                }

                Process process = new System.Diagnostics.Process();
                process.StartInfo.FileName = ERASER_PATH;
                process.StartInfo.Arguments = ERASER_ARGUMENTS + '"' + path + '"'; //argument
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                process.StartInfo.CreateNoWindow = true; //not diplay a windows
                process.StartInfo.Verb = "runas";
                process.Start();

                //process.StandardOutput.ReadToEnd();
                // El ERASER no termina explicitamente, se espera a que se borre el archivo y se termina desde invocador
                //process.WaitForExit();                                
                while (File.Exists(path))
                {
                    Thread.Sleep(2000);
                }
                if (!process.HasExited)
                {
                    process.Kill();
                    string stdoutx = process.StandardOutput.ReadToEnd();
                    string stderrx = process.StandardError.ReadToEnd();

                    if (process.ExitCode == 1)
                    {
                        Log(stderrx, EventLogEntryType.Error);
                    }
                    else
                    {
                        string output = "Archivo " + path + " borrado exitosamente."; //The output result                
                        Log(output, EventLogEntryType.Information);
                    }
                }

            }
            catch (Exception e)
            {                
                Log(e.Message, EventLogEntryType.Error);
            }

        }

        static bool IsElegibleForErasure(string path)
        {
            var fi1 = new FileInfo(path);
            DateTime processingStart = File.GetLastAccessTime(path);

            //Si el archivo está siendo usado por otra aplicación
            if (IsFileLocked(path))
            {                                
                //Si ha pasado más del tiempo de procesamiento excepcional, borrar
                if ((NOW - processingStart).TotalMinutes > EXCEPTIONAL_TIME)
                {
                    return true;
                }
                else
                {
                    return false;
                }                
            }            
            else
            {                
                //Si el archivo no está siendo usado por otra aplicación
                if ((NOW - processingStart).TotalMinutes > NORMAL_TIME)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }            
        }

        public static bool IsFileLocked(string filename)
        {
            bool Locked = false;
            try
            {
                FileStream fs =
                    File.Open(filename, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                fs.Close();
            }
            catch (IOException ex)
            {
                Locked = true;
            }
            return Locked;
        }


        static void Log(String message, EventLogEntryType level)
        {
            using (EventLog eventLog = new EventLog("Application"))
            {                
                string sSource = "BorradoSeguroSicore";
                string sLog = "Application";

                EventInstance eventInstance = new EventInstance(0, 0, level);
                List<string> sEvent = new List<string>();                

                if (File.Exists(message))
                {
                    var fi1 = new FileInfo(message);

                    sEvent.Add("Archivo:" + fi1.Name);
                    sEvent.Add("Directorio: " + fi1.DirectoryName);
                    sEvent.Add("Creado: " + fi1.CreationTime);
                    sEvent.Add("Última modificación: " + fi1.LastWriteTime);
                    sEvent.Add("Última modificación: " + fi1.LastWriteTime);
                }
                else
                {
                    sEvent.Add(message);
                }                

                // Check if Event Source was created (Possibly throw error if you are not running with high privilege)
                if (!EventLog.SourceExists(sSource))
                {
                    EventLog.CreateEventSource(sSource, sLog);
                }                    

                EventLog.WriteEvent(sSource, eventInstance, sEvent.ToArray());
            }
        }

    }
}
