using Game.Asset.Cli;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try { return await CommandLine.RunAsync(args, cancellation.Token); }
catch (OperationCanceledException) { Console.Error.WriteLine("Build canceled."); return 130; }
catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Security.Cryptography.CryptographicException or FormatException or OverflowException)
{ Console.Error.WriteLine(e.Message); return 1; }
