using Practice;

// One program, four modes:
//   web     the site you open in the browser; saves your work
//   runner  compiles and judges code; runs in a container without internet
//   child   started by the runner, once per run, to execute the submitted code
//   verify  checks that reference solutions pass and known-bad ones fail
switch (args.FirstOrDefault())
{
    case "web": WebApp.Run(); return 0;
    case "runner": RunnerApp.Run(); return 0;
    case "child": return Child.Run();
    case "verify": return await Verify.RunAsync(args.ElementAtOrDefault(1) ?? "/verify");
    default:
        Console.Error.WriteLine("Usage: Practice web|runner|verify");
        return 1;
}
