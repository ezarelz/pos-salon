// Entry point of the test runner.
//   dotnet run --project tests/MSPASalon.Tests            → all tests
//   dotnet run --project tests/MSPASalon.Tests -- INT-    → only tests whose ID starts with "INT-"
return MSPASalon.Tests.Framework.TestRunner.Run(args);
