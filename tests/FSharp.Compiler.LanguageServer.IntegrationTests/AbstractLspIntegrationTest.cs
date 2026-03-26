// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using FSharp.Compiler.LanguageServer.IntegrationTests.InProcess;
using Microsoft.VisualStudio.Extensibility.Testing;
using Xunit;

namespace FSharp.Compiler.LanguageServer.IntegrationTests;

[IdeSettings(MinVersion = VisualStudioVersion.VS2022)]
public abstract class AbstractLspIntegrationTest : AbstractIdeIntegrationTest
{
    protected CancellationToken TestToken => HangMitigatingCancellationToken;

    internal SolutionExplorerInProcess SolutionExplorer => TestServices.SolutionExplorer;
    internal EditorInProcess Editor => TestServices.Editor;
    internal ShellInProcess Shell => TestServices.Shell;
    internal ErrorListInProcess ErrorList => TestServices.ErrorList;
}
