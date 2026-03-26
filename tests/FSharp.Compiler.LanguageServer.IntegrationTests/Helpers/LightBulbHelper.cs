// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FSharp.Compiler.LanguageServer.IntegrationTests.Helpers;

internal static class LightBulbHelper
{
    public static async Task<IEnumerable<SuggestedActionSet>> WaitForItemsAsync(
        ILightBulbBroker broker,
        IWpfTextView view,
        CancellationToken cancellationToken)
    {
        var activeSession = broker.GetSession(view);
        var asyncSession = (IAsyncLightBulbSession)activeSession;
        var tcs = new TaskCompletionSource<IEnumerable<SuggestedActionSet>>();

        void Handler(object s, SuggestedActionsUpdatedArgs e)
        {
            if (e.Status == QuerySuggestedActionCompletionStatus.InProgress)
            {
                return;
            }

            if (e.Status == QuerySuggestedActionCompletionStatus.Completed ||
                e.Status == QuerySuggestedActionCompletionStatus.CompletedWithoutData)
            {
                tcs.SetResult(e.ActionSets);
            }
            else
            {
                tcs.SetException(new InvalidOperationException($"Light bulb transitioned to non-complete state: {e.Status}"));
            }

            asyncSession.SuggestedActionsUpdated -= Handler;
        }

        asyncSession.SuggestedActionsUpdated += Handler;

        asyncSession.Dismissed += (_, _) => tcs.TrySetCanceled(new CancellationToken(true));

        if (asyncSession.IsDismissed)
        {
            tcs.TrySetCanceled(new CancellationToken(true));
        }

        await asyncSession.PopulateWithDataAsync(overrideRequestedActionCategories: null, operationContext: null).ConfigureAwait(false);

        return await tcs.Task.WithCancellation(cancellationToken);
    }
}
