using FindThatBook.Eval;
using FindThatBook.Eval.Cli;
using FindThatBook.Eval.Datasets;

// Find That Book eval: run | compare | stability | record | fixtures. See eval/README.md.
try
{
    FindThatBook.Eval.Hosting.CodeFingerprint.Capture(); // stamp results with the code as it was when the run started
    var args0 = Args.Parse(args, Commands.Flags);
    using var cancel = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
    return args0.Command switch
    {
        "run" => await Commands.RunAsync(args0, args, cancel.Token),
        "compare" => Commands.Compare(args0),
        "stability" => await Commands.StabilityAsync(args0, args, cancel.Token),
        "record" => await Commands.RecordAsync(args0, args, cancel.Token),
        "fixtures" => await Commands.FixturesAsync(args0, args, cancel.Token),
        "ablate" => await Commands.AblateAsync(args0, args, cancel.Token),
        "index" => Commands.Index(args0),
        "help" or "--help" or "-h" => Commands.Help(),
        var other => Commands.Help($"Unknown command '{other}'."),
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("cancelled");
    return 130;
}
catch (HeldOutRefusedException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or InvalidOperationException
                               or Microsoft.Extensions.Options.OptionsValidationException or AggregateException)
{
    Console.Error.WriteLine($"error: {ex.Message}"); // e.g. a --config value the API's options validation rejects
    return 1;
}
