// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using JetBrains.Annotations;
using Microsoft.Data.Entity.Infrastructure;
using Microsoft.Data.Entity.Metadata;
using Microsoft.Data.Entity.Metadata.Internal;
using Microsoft.Data.Entity.Storage;

namespace Microsoft.Data.Entity.ChangeTracking.Internal
{
    public class SimpleKeyValueFactory<TKey> : KeyValueFactory
    {
        private readonly int _index;

        public SimpleKeyValueFactory([NotNull] IKey key)
            : base(key)
        {
            _index = key.Properties[0].GetIndex();
        }

        public override IKeyValue Create(ValueBuffer valueBuffer, int offset = 0) 
            => Create(valueBuffer[offset + _index]);

        public override IKeyValue Create(
            IReadOnlyList<IProperty> properties, ValueBuffer valueBuffer)
            => Create(valueBuffer[properties[0].GetIndex()]);

        public override IKeyValue Create(
            IReadOnlyList<IProperty> properties, IPropertyAccessor propertyAccessor)
            => Create(propertyAccessor[properties[0]]);

        private KeyValue Create(object value)
            => value != null
                ? new SimpleKeyValue<TKey>(Key, (TKey)value)
                : KeyValue.InvalidKeyValue;
    }
}
