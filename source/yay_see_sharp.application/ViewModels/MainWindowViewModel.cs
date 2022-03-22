using CliWrap;
using CliWrap.Buffered;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using yay_see_sharp.application.Properties;
using yay_see_sharp.application.Extenstions;
using yay_see_sharp.application.Helpers;

namespace yay_see_sharp.application.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public string Greeting => "Welcome to Avalonia!";

        public string ApplicationName => Constants.GetApplicationName();

        public MainWindowViewModel()
        {
            var output = new StringBuilder();
            var result = Cli.Wrap("cmd")
                .WithWorkingDirectory(@"C:\Users\miros")
                .WithArguments("/C dir")
                //.WithArguments(command => command.Add("/C dir"))
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(output))
                .ExecuteAsync();
            //var data = result.GetAwaiter().GetResult().StandardOutput;
            var value = output.ToString();



            //string command = "dir";

            //var startInfo = new ProcessStartInfo
            //{
            //    FileName = "cmd",
            //    Verb = "runas",
            //    Arguments = "/C "+command,
            //    //CreateNoWindow = false,
            //    RedirectStandardOutput = true,
            //    RedirectStandardInput = true,
            //    //WindowStyle = ProcessWindowStyle.Hidden,
            //    UseShellExecute = false
            //};

            //var cmd = Process.Start(startInfo);

            //string output = cmd.StandardOutput.ReadToEnd();

            //cmd.StandardInput.WriteLine("/C dir");
            //output = cmd.StandardOutput.ReadToEnd();



            //using(var stringWritter = new StringWriter())
            //{
            //    Console.SetOut(stringWritter);
            //    Console.WriteLine("dir");
            //    Console.
            //    var data = stringWritter.ToString();
            //}
        }
    }
}
