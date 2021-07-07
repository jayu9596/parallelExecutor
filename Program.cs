using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;

class ExecuteProcessClass
{
    public static string corralExecutablePath;
    public static string folderPath;
    public static int totalParallelProcess = 8;
    public static int timeToVerify = 3600;
    public static int bufferTime = 60;
    public static int alphaInterleaving = 100;
    public static int recursionBound = 3;
    public static string arguments = " /si /oldCorralFlags /useProverEvaluate /recursionBound:"+ recursionBound + " /alphaInterleaving:" + alphaInterleaving + " /killAfter:" + (timeToVerify + bufferTime).ToString();
    public static List<Process> corralProcessList;
    public static Dictionary<Process, DateTime> startTime;
    public static Dictionary<Process, StreamWriter> outputWriter;
    public static string[] filePaths;
    public static Queue<string> fileQueue;
    public static TimeSpan timeout = new TimeSpan();

    // Print a file with any known extension.
    public void startVerification()
    {
        string inputFilesDirectory = folderPath;
        filePaths = Directory.GetFiles(inputFilesDirectory, "*.bpl");
        fileQueue = new Queue<string>(filePaths);
        while (true)
        {
            if (corralProcessList.Count < totalParallelProcess && fileQueue.Count > 0)
            {
                int trial = 1;
                string filename = fileQueue.Dequeue();
                while(trial < 3)
                {
                    trial++;
                    if (runCorral(filename))
                    {
                        Console.WriteLine("Started: " + filename + " at " + DateTime.Now);
                        break;
                    }
                    else
                    {
                        Console.WriteLine("Retrying: " + filename + " trial: " + (trial-1).ToString());
                    }
                }
            }
            removeCompletedProcess();
            removeTimedoutProcess();
            killZ3ProcessAccToTimeTaken();
            if (corralProcessList.Count == 0 && fileQueue.Count == 0)
                break;
        }
        Console.WriteLine("Parallel Execution Finished");
    }

    public static void killZ3ProcessAccToTimeTaken()
    {
        //kill z3 processes whose elapsed time is more than timeToVerify 
        string stdout;
        RunProcessAndWaitForExit("sh", "killZ3Process.sh", timeout,out stdout);
    }

    public static void removeCompletedProcess()
    {
        List<Process> processesToRemove = new List<Process>();
        foreach(var p in corralProcessList)
        {
            if (p.HasExited)
            {
                processesToRemove.Add(p);
                if (outputWriter.ContainsKey(p))
                {
                    outputWriter[p].WriteLine("exitcode: " + p.ExitCode);
                    outputWriter[p].Close();
                    outputWriter.Remove(p);
                }
                if (startTime.ContainsKey(p))
                    startTime.Remove(p);
            }
        }
        foreach (var p in processesToRemove)
        {
            string outputFilename = p.StartInfo.Arguments.Split(' ')[1];
            corralProcessList.Remove(p);
            Console.WriteLine("Completed: " + outputFilename);
        }
    }

    public static void removeTimedoutProcess()
    {
        List<Process> processesToRemove = new List<Process>();
        foreach (var p in corralProcessList)
        {
            double totalSeconds = (DateTime.Now - startTime[p]).TotalSeconds;
            if(totalSeconds > timeToVerify)
            {
                processesToRemove.Add(p);
                killProcessSubTree(p);
                if (!p.HasExited)
                    p.Kill();
                if (startTime.ContainsKey(p))
                    startTime.Remove(p);
                if (outputWriter.ContainsKey(p))
                {
                    outputWriter[p].Close();
                    outputWriter.Remove(p);
                    string outputFilename = p.StartInfo.Arguments.Split(' ')[1] + ".txt";
                    File.WriteAllText(outputFilename, "Corral timed out");
                }
            }
        }
        foreach (var p in processesToRemove)
        {
            string outputFilename = p.StartInfo.Arguments.Split(' ')[1];
            corralProcessList.Remove(p);
            Console.WriteLine("TimedOut: " + outputFilename);
        }

    }

    static void killProcessSubTree(Process p)
    {
        HashSet<int> z3Process = new HashSet<int>();
        getZ3ProcessIds(p.Id, z3Process, timeout);
        foreach (var pid in z3Process)
        {
            killProcess(pid, timeout);
        }
    }

    static void getZ3ProcessIds(int pid, ISet<int> z3Process, TimeSpan timeout)
    {
        string stdout;
        var exitCode = RunProcessAndWaitForExit("pgrep", $"-P {pid}", timeout, out stdout);

        if (exitCode == 0 && !string.IsNullOrEmpty(stdout))
        {
            using (var reader = new StringReader(stdout))
            {
                while (true)
                {
                    var text = reader.ReadLine();
                    if (text == null)
                    {
                        return;
                    }

                    int id;
                    if (int.TryParse(text, out id))
                    {
                        z3Process.Add(id);
                        // Recursively get the children                            
                    }
                }
            }
        }
    }

    static void killProcess(int pid, TimeSpan timeout)
    {
        string stdout;
        RunProcessAndWaitForExit("kill", $"-TERM {pid}", timeout, out stdout);
    }

    static int RunProcessAndWaitForExit(string fileName, string arguments, TimeSpan timeout, out string stdout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        var process = Process.Start(startInfo);
        //Console.WriteLine("command : " + fileName + " " + arguments);
        stdout = null;
        if (process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            stdout = process.StandardOutput.ReadToEnd();
        }
        else
        {
            Console.WriteLine("Kill process did not finish");
        }

        return process.ExitCode;
    }


    public static bool runCorral(string fileName)
    {
        try
        {
            Process p = new Process();
            var outputStream = new StreamWriter(fileName + ".txt");
            p.StartInfo.FileName = "mono";
            p.StartInfo.Arguments = corralExecutablePath + " " + fileName + arguments;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    outputStream.WriteLine(e.Data);
                }
            });

            p.Start();
            p.BeginOutputReadLine();
            startTime.Add(p, DateTime.Now);
            corralProcessList.Add(p);
            outputWriter.Add(p, outputStream);
        }
        catch(Exception e)
        {
            Console.WriteLine("ERROR while starting " + fileName);
            Console.WriteLine(e.Message);
            return false;
        }
        return true;
    }

    public static void setupZ3KillScript()
    {
        File.WriteAllText("killZ3Process.sh", $"kill -9 $(ps -eo comm,pid,etimes | awk '/^z3/ {{if ($3 > " + (timeToVerify + bufferTime).ToString() + $") {{ print $2}}}}') > /dev/null 2>&1");
    }

    public static void printConfig()
    {
        Console.WriteLine("#################### Config ####################");
        Console.WriteLine("Max Parallel Process: " + totalParallelProcess);
        Console.WriteLine("Timeout: " + timeToVerify);
        Console.WriteLine("AlphaInterLeaving: " + alphaInterleaving);
        Console.WriteLine("Recursion Bound: " + recursionBound);
        Console.WriteLine("Folder Path: " + folderPath);
        Console.WriteLine("Corral Path: " + corralExecutablePath);
        Console.WriteLine("################################################");
    }

    public static void Main(string[] args)
    {
        // Verify that an argument has been entered.
        if (args.Length <= 1)
        {
            Console.WriteLine("Enter corral and folder path");
            return;
        }

        // Initial configurations
        corralExecutablePath = args[0];
        folderPath = args[1];
        corralProcessList = new List<Process>();
        startTime = new Dictionary<Process, DateTime>();
        outputWriter = new Dictionary<Process, StreamWriter>();
        timeout = TimeSpan.FromMilliseconds(10000);
        setupZ3KillScript();
        ExecuteProcessClass myExecute = new ExecuteProcessClass();

        printConfig();

        //Start Parallel Verification
        myExecute.startVerification();
    }
}