using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;

class ExecuteProcessClass
{
    public static string corralExecutablePath;
    public static string folderPath;
    public static int totalParallelProcess = 1;
    public static int timeToVerify = 900;
    public static int bufferTime = 60;
    public static int relaxTime = 10;
    public static int alphaInterleaving = 100;
    public static int recursionBound = 3;
    public static string arguments = "/si /oldCorralFlags /useProverEvaluate /recursionBound:"+ recursionBound + " /alphaInterleaving:" + alphaInterleaving + " /killAfter:" + timeToVerify;
    public static string[] filePaths;
    public static Queue<string> fileQueue;
    public static Process lastProcess;
    public static string memoryTrackingPath;
    public void startVerification()
    {
        string inputFilesDirectory = folderPath;
        filePaths = Directory.GetFiles(inputFilesDirectory, "*.bpl");
        fileQueue = new Queue<string>(filePaths);
        DateTime lastZ3KillTime = DateTime.Now;
        List<string> bashFiles = new List<string>();
        int bashFileCount = 0;
        int sleepTime = 1;
        while (fileQueue.Count > 0)
        {
            int cnt = 0;
            string currBashFile = "runBatch_" + bashFileCount + ".sh";
            Console.WriteLine("#################################################");
            Console.WriteLine("batch " + bashFileCount + " at delay: " + sleepTime + " seconds");
            bashFiles.Add(currBashFile);
            File.WriteAllText(currBashFile, "sleep " + sleepTime + "\n");
            //File.AppendAllText(currBashFile, "echo \"" + currBashFile + "started\"\n");
            while (fileQueue.Count > 0)
            {
                string filename = fileQueue.Dequeue();
                string command = "nohup mono " + corralExecutablePath + " " + filename + " " + arguments + " > " + filename + ".txt &\n";
                File.AppendAllText(currBashFile, command);

                Console.WriteLine(filename);
                cnt++;
                if (totalParallelProcess == 1)
                {
                    string comm = "sleep " + relaxTime + "\n";
                    File.AppendAllText(currBashFile, comm);
                    comm = "nohup mono " + memoryTrackingPath + " 1 > " + filename + "_memory.txt &\n";
                    File.AppendAllText(currBashFile, comm);
                }
                if (cnt >= totalParallelProcess)
                    break;

            }
            //Kill z3 processes
            File.AppendAllText(currBashFile, "sleep " + (timeToVerify + relaxTime) + "\n");
            string killZ3 = "pkill z3\n";
            File.AppendAllText(currBashFile, killZ3);

            bashFileCount++;
            sleepTime += (timeToVerify + bufferTime + relaxTime);
        }
        Console.WriteLine("#################################################");

        foreach (var bashfile in bashFiles)
        {
            if(runBashFile(bashfile))
            {
                Console.WriteLine(bashfile + " scheduled");
            }
            else
            {
                Console.WriteLine("Error in scheduling " + bashfile);
            }
        }
        Console.WriteLine("Parallel Schedule Finished");
        lastProcess.WaitForExit(-1);
        Console.WriteLine("All Executions completed");
    }

    public static bool runBashFile(string fileName)
    {
        try
        {
            Process p = new Process();
            p.StartInfo.FileName = "sh";
            p.StartInfo.Arguments = fileName;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.Start();
            lastProcess = p;
        }
        catch(Exception e)
        {
            Console.WriteLine("ERROR while executing " + fileName);
            Console.WriteLine(e.Message);
            return false;
        }

        return true;
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
        Console.WriteLine("################################################\n");
    }

    public static void Main(string[] args)
    {
        // Verify that an argument has been entered.
        if (args.Length <= 1)
        {
            Console.WriteLine("Enter corral executable and folder path");
            return;
        }

        // Initial configurations
        corralExecutablePath = args[0];
        folderPath = args[1];
        if(args.Length == 3)
        {
            memoryTrackingPath = args[2];
        }
        ExecuteProcessClass myExecute = new ExecuteProcessClass();

        printConfig();

        //Start Parallel Verification
        myExecute.startVerification();
    }
}