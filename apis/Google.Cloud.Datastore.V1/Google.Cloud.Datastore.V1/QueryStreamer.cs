﻿// Copyright 2016 Google Inc. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static Google.Cloud.Datastore.V1.QueryResultBatch.Types;

namespace Google.Cloud.Datastore.V1
{
    /// <summary>
    /// Creates a stream of responses (synchronous and asynchronous) from a query.
    /// </summary>
    internal class QueryStreamer
    {
        private readonly RunQueryRequest _initialRequest;
        private readonly ApiCall<RunQueryRequest, RunQueryResponse> _apiCall;
        private readonly CallSettings _callSettings;

        internal QueryStreamer(RunQueryRequest initialRequest, ApiCall<RunQueryRequest, RunQueryResponse> apiCall, CallSettings callSettings)
        {
            _initialRequest = GaxPreconditions.CheckNotNull(initialRequest, nameof(initialRequest)).Clone();
            _apiCall = GaxPreconditions.CheckNotNull(apiCall, nameof(apiCall));
            _callSettings = callSettings;
        }

        // The real business logic of this class is all in the following two methods. The rest is pretty much boilerplate.

        private static void ModifyRequest(RunQueryRequest request, RunQueryResponse response)
        {
            // Transition from GQL to structured queries.
            if (response.Query != null)
            {
                request.Query = response.Query;
            }
            // Offset/limit/cursor handling.
            var query = request.Query;
            var batch = response.Batch;
            query.Offset -= batch.SkippedResults;
            if (query.Limit > 0)
            {
                query.Limit -= batch.EntityResults.Count;
            }
            query.StartCursor = batch.EndCursor;
        }

        private static bool MoreResultsAvailable(RunQueryResponse response) =>
            response.Batch.MoreResults == MoreResultsType.NotFinished;

        internal IAsyncEnumerable<RunQueryResponse> Async() => new AsyncQueryEnumerable(this);

        internal IEnumerable<RunQueryResponse> Sync()
        {
            var request = _initialRequest.Clone();
            RunQueryResponse response;
            do
            {
                response = _apiCall.Sync(request, _callSettings);
                yield return response;
                ModifyRequest(request, response);
            } while (MoreResultsAvailable(response));
        }

        private class AsyncQueryEnumerable : IAsyncEnumerable<RunQueryResponse>
        {
            private readonly QueryStreamer _parent;

            internal AsyncQueryEnumerable(QueryStreamer parent)
            {
                _parent = parent;
            }

            public IAsyncEnumerator<RunQueryResponse> GetEnumerator()
                => new AsyncQueryEnumerator(_parent);

            private class AsyncQueryEnumerator : IAsyncEnumerator<RunQueryResponse>
            {
                private readonly QueryStreamer _parent;
                private RunQueryRequest _request;
                private bool _finished;

                public AsyncQueryEnumerator(QueryStreamer _parent)
                {
                    this._parent = _parent;
                    _request = _parent._initialRequest.Clone();
                }

                public RunQueryResponse Current { get; private set; }

                public async Task<bool> MoveNext(CancellationToken cancellationToken)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_finished)
                    {
                        return false;
                    }
                    CallSettings effectiveCallSettings = _parent._callSettings;
                    if (cancellationToken != default(CancellationToken))
                    {
                        effectiveCallSettings = effectiveCallSettings.WithCancellationToken(cancellationToken);
                    }
                    var response = await _parent._apiCall.Async(_request, effectiveCallSettings);
                    _finished = !MoreResultsAvailable(response);
                    ModifyRequest(_request, response);
                    Current = response;
                    return true;
                }

                public void Dispose() { }
            }
        }
    }
}
