# Diagnostic examples

Each `.cs.txt` file here is a small program that triggers exactly one HttpKit diagnostic. They are `.txt`
so the sample does not try to compile them; `DiagnosticExampleTests` in `AwadyLab.HttpKit.Generators.Tests`
compiles each one and asserts that the diagnostic named by the file still fires.

That makes the diagnostics reference in the [HttpKit README](../../README.md#diagnostics-analyzers--code-fixes) executable: if a rule stops
firing, or starts firing under a different ID, the test fails rather than the documentation quietly going
stale.
