// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Metadata.Internal
{
    public class ViewType : ConventionalAnnotatable, IViewType
    {
        private readonly SortedDictionary<string, Property> _properties
            = new SortedDictionary<string, Property>();

        private ConfigurationSource _configurationSource;
        private InternalViewTypeBuilder _builder;

        public ViewType([NotNull] Type clrType, [NotNull] Model model, ConfigurationSource configurationSource)
            : this(model, configurationSource)
        {
            Check.ValidEntityType(clrType, nameof(clrType)); // TODO: View not entity
            Check.NotNull(model, nameof(model));

            ClrType = clrType;
#if DEBUG
            DebugName = this.DisplayName();
#endif
        }

        private ViewType([NotNull] Model model, ConfigurationSource configurationSource)
        {
            Model = model;

            _configurationSource = configurationSource;
            _builder = new InternalViewTypeBuilder(this, model.Builder);
        }

        public virtual string Name => ClrType.DisplayName();
        public virtual IModel Model { get; }
        public virtual Type ClrType { get; }

        public virtual InternalViewTypeBuilder Builder
        {
            get { return _builder; }
            [param: CanBeNull] set { _builder = value; }
        }

        IProperty IViewType.FindProperty(string name)
        {
            throw new NotImplementedException();
        }

        IEnumerable<IProperty> IViewType.GetProperties()
        {
            throw new NotImplementedException();
        }

#if DEBUG
        [UsedImplicitly]
        private string DebugName { get; set; }
#endif
    }
}
