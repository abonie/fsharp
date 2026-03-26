// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FSharp.Compiler.LanguageServer.IntegrationTests;

public class LightBulbCodeActionTests : AbstractLspIntegrationTest
{
    private const string CodeWithMissingNewKeyword = """
        module Test

        let x = System.IO.FileStream("dummy.txt", System.IO.FileMode.Create)
        """;

    private const string CodeWithEqualityOperator = """
        module Test

        let x = 1 == 2
        """;

    private const string CodeWithNotMutableAssignment = """
        module Test

        let x = 1
        x <- 2
        """;

    private const string CodeWithMissingFunKeyword = """
        module Test

        let f = x -> x + 1
        """;

    [IdeFact]
    public async Task ConstructorCall_OffersCodeActionToAddNewKeyword()
    {
        var template = WellKnownProjectTemplates.FSharpNetCoreClassLibrary;

        await SolutionExplorer.CreateSingleProjectSolutionAsync("TestProject", template, TestToken);
        await SolutionExplorer.RestoreNuGetPackagesAsync(TestToken);
        await Editor.SetTextAsync(CodeWithMissingNewKeyword, TestToken);
        await Editor.PlaceCaretAsync("FileStream", TestToken);

        var codeActions = await Editor.InvokeCodeActionListAsync(TestToken);

        var allActions = codeActions.SelectMany(set => set.Actions).ToList();
        Assert.True(
            allActions.Any(a => a.DisplayText.Contains("Add 'new' keyword")),
            $"Expected a code action containing \"Add 'new' keyword\" but found: {string.Join(", ", allActions.Select(a => a.DisplayText))}");
    }

    [IdeFact]
    public async Task EqualityOperator_OffersConvertToEquals()
    {
        var template = WellKnownProjectTemplates.FSharpNetCoreClassLibrary;

        await SolutionExplorer.CreateSingleProjectSolutionAsync("TestProject", template, TestToken);
        await SolutionExplorer.RestoreNuGetPackagesAsync(TestToken);
        await Editor.SetTextAsync(CodeWithEqualityOperator, TestToken);
        await Editor.PlaceCaretAsync("==", TestToken);

        var codeActions = await Editor.InvokeCodeActionListAsync(TestToken);

        var allActions = codeActions.SelectMany(set => set.Actions).ToList();
        Assert.True(
            allActions.Any(a => a.DisplayText.Contains("Use '=' for equality")),
            $"Expected a code action containing \"Use '=' for equality\" but found: {string.Join(", ", allActions.Select(a => a.DisplayText))}");
    }

    [IdeFact]
    public async Task NotMutableAssignment_OffersMakeDeclarationMutable()
    {
        var template = WellKnownProjectTemplates.FSharpNetCoreClassLibrary;

        await SolutionExplorer.CreateSingleProjectSolutionAsync("TestProject", template, TestToken);
        await SolutionExplorer.RestoreNuGetPackagesAsync(TestToken);
        await Editor.SetTextAsync(CodeWithNotMutableAssignment, TestToken);
        await Editor.PlaceCaretAsync("<-", TestToken);

        var codeActions = await Editor.InvokeCodeActionListAsync(TestToken);

        var allActions = codeActions.SelectMany(set => set.Actions).ToList();
        Assert.True(
            allActions.Any(a => a.DisplayText.Contains("mutable")),
            $"Expected a code action containing \"mutable\" but found: {string.Join(", ", allActions.Select(a => a.DisplayText))}");
    }

    [IdeFact]
    public async Task MissingFunKeyword_OffersAddFunKeyword()
    {
        var template = WellKnownProjectTemplates.FSharpNetCoreClassLibrary;

        await SolutionExplorer.CreateSingleProjectSolutionAsync("TestProject", template, TestToken);
        await SolutionExplorer.RestoreNuGetPackagesAsync(TestToken);
        await Editor.SetTextAsync(CodeWithMissingFunKeyword, TestToken);
        await Editor.PlaceCaretAsync("->", TestToken);

        var codeActions = await Editor.InvokeCodeActionListAsync(TestToken);

        var allActions = codeActions.SelectMany(set => set.Actions).ToList();
        Assert.True(
            allActions.Any(a => a.DisplayText.Contains("Add 'fun' keyword")),
            $"Expected a code action containing \"Add 'fun' keyword\" but found: {string.Join(", ", allActions.Select(a => a.DisplayText))}");
    }
}
