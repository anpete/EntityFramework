// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using JetBrains.Annotations;
using Microsoft.Data.Entity.Storage;

namespace Microsoft.Data.Entity.Query
{
    public struct EntityLoadInfo
    {
        private readonly Func<ValueBuffer, object> _materializer;

        public EntityLoadInfo(
            ValueBuffer valueBuffer, 
            [NotNull] Func<ValueBuffer, object> materializer, 
            int valueBufferOffset = 0)
        {
            // hot path
            Debug.Assert(materializer != null);

            ValueBuffer = valueBuffer;
            _materializer = materializer;
            ValueBufferOffset = valueBufferOffset;
        }

        public ValueBuffer ValueBuffer { get; }

        public int ValueBufferOffset { get; }

        public object Materialize() => _materializer(ValueBuffer);
    }
}
