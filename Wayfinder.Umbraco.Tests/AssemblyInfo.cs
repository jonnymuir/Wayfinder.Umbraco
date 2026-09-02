using Xunit;

// SupportSystemRegistry (Wayfinder.Models.ServiceDesign.SupportSystems) is process-wide static
// state that freezes the first time anything reads it. Disabling parallelization keeps a test
// that registers into it (ConfiguredSupportSystemBlueprintTests) from racing any other test in
// the assembly that reads it — the same reasoning Wayfinder.Tests' own AssemblyInfo documents
// for ComponentTypeRegistry.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
