﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.Commands;
using Microsoft.CodeAnalysis.Shared.TestHooks;

namespace Microsoft.CodeAnalysis.Editor.Implementation.IntelliSense.Completion
{
    internal partial class Controller
    {
        CommandState ICommandHandler<CommitUniqueCompletionListItemCommandArgs>.GetCommandState(CommitUniqueCompletionListItemCommandArgs args, Func<CommandState> nextHandler)
        {
            AssertIsForeground();
            return nextHandler();
        }

        void ICommandHandler<CommitUniqueCompletionListItemCommandArgs>.ExecuteCommand(
            CommitUniqueCompletionListItemCommandArgs args, Action nextHandler)
        {
            AssertIsForeground();

            if (sessionOpt == null)
            {
                // User hit ctrl-space.  If there was no completion up then we want to trigger
                // completion. 
                var completionService = this.GetCompletionService();
                if (completionService == null)
                {
                    return;
                }

                if (!StartNewModelComputation(completionService, filterItems: true, dismissIfEmptyAllowed: true))
                {
                    return;
                }
            }

            if (sessionOpt.InitialUnfilteredModel == null && !ShouldBlockForCompletionItems())
            {
                CommitUniqueCompletionListItemAsynchronously();
                return;
            }

            // Get the selected item.  If it's unique, then we want to commit it.
            var model = WaitForModel();
            if (model == null)
            {
                // Computation failed.  Just pass this command on.
                nextHandler();
                return;
            }

            CommitIfUnique(model);
        }

        private void CommitUniqueCompletionListItemAsynchronously()
        {
            // We're in a language that doesn't want to block, but hasn't computed the initial
            // set of completion items.  In this case, we asynchronously wait for the items to
            // be computed.  And if nothing has happened between now and that point, we proceed
            // with committing the items.
            var currentSession = sessionOpt;
            var currentTask = currentSession.Computation.ModelTask;

            // We're kicking off async work.  Track this with an async token for test purposes.
            var token = ((IController<Model>)this).BeginAsyncOperation(nameof(CommitUniqueCompletionListItemAsynchronously));

            var task = currentTask.ContinueWith(t =>
            {
                this.AssertIsForeground();

                if (this.sessionOpt == currentSession &&
                    this.sessionOpt.Computation.ModelTask == currentTask)
                {
                    // Nothing happened between when we were invoked and now.
                    CommitIfUnique(t.Result);
                }
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, this.ForegroundTaskScheduler);

            task.CompletesAsyncOperation(token);
        }

        private void CommitIfUnique(Model model)
        {
            // Note: Dev10 behavior seems to be that if there is no unique item that filtering is
            // turned off.  However, i do not know if this is desirable behavior, or merely a bug
            // with how that convoluted code worked.  So I'm not maintaining that behavior here.  If
            // we do want it through, it would be easy to get again simply by asking the model
            // computation to remove all filtering.
            if (model.IsUnique)
            {
                // We had a unique item in the list.  Commit it and dismiss this session.
                this.CommitOnNonTypeChar(model.SelectedItem, model);
            }
        }
    }
}