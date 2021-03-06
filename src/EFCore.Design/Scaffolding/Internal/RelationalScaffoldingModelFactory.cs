// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Scaffolding.Internal
{
    public class RelationalScaffoldingModelFactory : IScaffoldingModelFactory
    {
        internal const string NavigationNameUniquifyingPattern = "{0}Navigation";
        internal const string SelfReferencingPrincipalEndNavigationNamePattern = "Inverse{0}";

        protected virtual IDiagnosticsLogger<DbLoggerCategory.Scaffolding> Logger { get; }
        protected virtual IRelationalTypeMapper TypeMapper { get; }
        protected virtual ICandidateNamingService CandidateNamingService { get; }

        private Dictionary<TableModel, CSharpUniqueNamer<ColumnModel>> _columnNamers;
        private readonly TableModel _nullTable = new TableModel();
        private CSharpUniqueNamer<TableModel> _tableNamer;
        private readonly IDatabaseModelFactory _databaseModelFactory;
        private readonly HashSet<ColumnModel> _unmappedColumns = new HashSet<ColumnModel>();
        private readonly IPluralizer _pluralizer;
        private readonly IScaffoldingProviderCodeGenerator _providerCodeGenerator;
        private readonly ICSharpUtilities _cSharpUtilities;
        private readonly IScaffoldingTypeMapper _scaffoldingTypeMapper;

        public RelationalScaffoldingModelFactory(
            [NotNull] IDiagnosticsLogger<DbLoggerCategory.Scaffolding> logger,
            [NotNull] IRelationalTypeMapper typeMapper,
            [NotNull] IDatabaseModelFactory databaseModelFactory,
            [NotNull] ICandidateNamingService candidateNamingService,
            [NotNull] IPluralizer pluralizer,
            [NotNull] IScaffoldingProviderCodeGenerator providerCodeGenerator,
            [NotNull] ICSharpUtilities cSharpUtilities,
            [NotNull] IScaffoldingTypeMapper scaffoldingTypeMapper)
        {
            Check.NotNull(logger, nameof(logger));
            Check.NotNull(typeMapper, nameof(typeMapper));
            Check.NotNull(databaseModelFactory, nameof(databaseModelFactory));
            Check.NotNull(candidateNamingService, nameof(candidateNamingService));
            Check.NotNull(pluralizer, nameof(pluralizer));
            Check.NotNull(providerCodeGenerator, nameof(providerCodeGenerator));
            Check.NotNull(cSharpUtilities, nameof(cSharpUtilities));
            Check.NotNull(scaffoldingTypeMapper, nameof(scaffoldingTypeMapper));

            Logger = logger;
            TypeMapper = typeMapper;
            CandidateNamingService = candidateNamingService;
            _databaseModelFactory = databaseModelFactory;
            _pluralizer = pluralizer;
            _providerCodeGenerator = providerCodeGenerator;
            _cSharpUtilities = cSharpUtilities;
            _scaffoldingTypeMapper = scaffoldingTypeMapper;
        }

        public virtual IModel Create(string connectionString, IEnumerable<string> tables, IEnumerable<string> schemas)
        {
            Check.NotEmpty(connectionString, nameof(connectionString));
            Check.NotNull(tables, nameof(tables));
            Check.NotNull(schemas, nameof(schemas));

            var databaseModel = _databaseModelFactory.Create(connectionString, tables, schemas);

            return CreateFromDatabaseModel(databaseModel);
        }

        protected virtual IModel CreateFromDatabaseModel([NotNull] DatabaseModel databaseModel)
        {
            Check.NotNull(databaseModel, nameof(databaseModel));

            var modelBuilder = new ModelBuilder(new ConventionSet());

            _tableNamer = new CSharpUniqueNamer<TableModel>(
                t => CandidateNamingService.GenerateCandidateIdentifier(t.Name),
                _cSharpUtilities);
            _columnNamers = new Dictionary<TableModel, CSharpUniqueNamer<ColumnModel>>();

            VisitDatabaseModel(modelBuilder, databaseModel);

            return modelBuilder.Model;
        }

        protected virtual string GetEntityTypeName([NotNull] TableModel table)
            => _pluralizer.Singularize(_tableNamer.GetName(Check.NotNull(table, nameof(table))));

        protected virtual string GetDbSetName([NotNull] TableModel table)
            => _pluralizer.Pluralize(_tableNamer.GetName(Check.NotNull(table, nameof(table))));

        protected virtual string GetPropertyName([NotNull] ColumnModel column)
        {
            Check.NotNull(column, nameof(column));

            var table = column.Table ?? _nullTable;
            var usedNames = new List<string>();
            // TODO - need to clean up the way CSharpNamer & CSharpUniqueNamer work (see issue #1671)
            if (column.Table != null)
            {
                usedNames.Add(_tableNamer.GetName(table));
            }

            if (!_columnNamers.ContainsKey(table))
            {
                _columnNamers.Add(
                    table,
                    new CSharpUniqueNamer<ColumnModel>(
                        c => CandidateNamingService.GenerateCandidateIdentifier(c.Name), usedNames, _cSharpUtilities));
            }

            return _columnNamers[table].GetName(column);
        }

        protected virtual ModelBuilder VisitDatabaseModel([NotNull] ModelBuilder modelBuilder, [NotNull] DatabaseModel databaseModel)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));
            Check.NotNull(databaseModel, nameof(databaseModel));

            if (!string.IsNullOrEmpty(databaseModel.DefaultSchemaName))
            {
                modelBuilder.HasDefaultSchema(databaseModel.DefaultSchemaName);
            }

            if (!string.IsNullOrEmpty(databaseModel.DatabaseName))
            {
                modelBuilder.Model.Scaffolding().DatabaseName = databaseModel.DatabaseName;
            }

            VisitSequences(modelBuilder, databaseModel.Sequences);
            VisitTables(modelBuilder, databaseModel.Tables);
            VisitForeignKeys(modelBuilder, databaseModel.Tables.SelectMany(table => table.ForeignKeys).ToList());

            modelBuilder.Model.AddAnnotations(databaseModel.GetAnnotations());

            return modelBuilder;
        }

        protected virtual ModelBuilder VisitSequences([NotNull] ModelBuilder modelBuilder, [NotNull] ICollection<SequenceModel> sequences)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));
            Check.NotNull(sequences, nameof(sequences));

            foreach (var sequence in sequences)
            {
                VisitSequence(modelBuilder, sequence);
            }

            return modelBuilder;
        }

        protected virtual SequenceBuilder VisitSequence([NotNull] ModelBuilder modelBuilder, [NotNull] SequenceModel sequence)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));
            Check.NotNull(sequence, nameof(sequence));

            if (string.IsNullOrEmpty(sequence.Name))
            {
                Logger.SequenceNotNamedWarning();
                return null;
            }

            Type sequenceType = null;
            if (sequence.DataType != null)
            {
                sequenceType = TypeMapper.FindMapping(sequence.DataType)?.ClrType;
            }

            if (sequenceType != null
                && !Sequence.SupportedTypes.Contains(sequenceType))
            {
                Logger.SequenceTypeNotSupportedWarning(sequence.Name, sequence.DataType);
                return null;
            }

            var builder = sequenceType != null
                ? modelBuilder.HasSequence(sequenceType, sequence.Name, sequence.SchemaName)
                : modelBuilder.HasSequence(sequence.Name, sequence.SchemaName);

            if (sequence.IncrementBy.HasValue)
            {
                builder.IncrementsBy(sequence.IncrementBy.Value);
            }

            if (sequence.Max.HasValue)
            {
                builder.HasMax(sequence.Max.Value);
            }

            if (sequence.Min.HasValue)
            {
                builder.HasMin(sequence.Min.Value);
            }

            if (sequence.Start.HasValue)
            {
                builder.StartsAt(sequence.Start.Value);
            }

            if (sequence.IsCyclic.HasValue)
            {
                builder.IsCyclic(sequence.IsCyclic.Value);
            }

            return builder;
        }

        protected virtual ModelBuilder VisitTables([NotNull] ModelBuilder modelBuilder, [NotNull] ICollection<TableModel> tables)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));
            Check.NotNull(tables, nameof(tables));

            foreach (var table in tables)
            {
                VisitTable(modelBuilder, table);
            }

            return modelBuilder;
        }

        protected virtual EntityTypeBuilder VisitTable([NotNull] ModelBuilder modelBuilder, [NotNull] TableModel table)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));
            Check.NotNull(table, nameof(table));

            var entityTypeName = GetEntityTypeName(table);

            var builder = modelBuilder.Entity(entityTypeName);

            var dbSetName = GetDbSetName(table);
            builder.Metadata.Scaffolding().DbSetName = dbSetName;

            builder.ToTable(table.Name, table.SchemaName);

            VisitColumns(builder, table.Columns);

            var keyBuilder = VisitPrimaryKey(builder, table);

            if (keyBuilder == null)
            {
                var errorMessage = DesignStrings.LogUnableToGenerateEntityType.GenerateMessage(table.DisplayName);
                Logger.UnableToGenerateEntityTypeWarning(table.DisplayName);

                var model = modelBuilder.Model;
                model.RemoveEntityType(entityTypeName);
                model.Scaffolding().EntityTypeErrors.Add(entityTypeName, errorMessage);
                return null;
            }

            VisitIndexes(builder, table.Indexes);

            builder.Metadata.AddAnnotations(table.GetAnnotations());

            return builder;
        }

        protected virtual EntityTypeBuilder VisitColumns([NotNull] EntityTypeBuilder builder, [NotNull] ICollection<ColumnModel> columns)
        {
            Check.NotNull(builder, nameof(builder));
            Check.NotNull(columns, nameof(columns));

            foreach (var column in columns)
            {
                VisitColumn(builder, column);
            }

            return builder;
        }

        protected virtual PropertyBuilder VisitColumn([NotNull] EntityTypeBuilder builder, [NotNull] ColumnModel column)
        {
            Check.NotNull(builder, nameof(builder));
            Check.NotNull(column, nameof(column));

            var typeScaffoldingInfo = GetTypeScaffoldingInfo(column);

            if (typeScaffoldingInfo == null)
            {
                _unmappedColumns.Add(column);
                Logger.ColumnTypeNotMappedWarning(column.DisplayName, column.StoreType);
                return null;
            }

            var clrType = typeScaffoldingInfo.ClrType;
            var forceNullable = typeof(bool) == clrType && column.DefaultValue != null;
            if (forceNullable)
            {
                Logger.NonNullableBoooleanColumnHasDefaultConstraintWarning(
                    column.DisplayName);
            }
            if (column.IsNullable || forceNullable)
            {
                clrType = clrType.MakeNullable();
            }

            var property = builder.Property(clrType, GetPropertyName(column));

            property.HasColumnName(column.Name);

            if (!typeScaffoldingInfo.IsInferred && !string.IsNullOrWhiteSpace(column.StoreType))
            {
                property.HasColumnType(column.StoreType);
            }

            if (typeScaffoldingInfo.ScaffoldUnicode.HasValue)
            {
                property.IsUnicode(typeScaffoldingInfo.ScaffoldUnicode.Value);
            }

            if (typeScaffoldingInfo.ScaffoldMaxLength.HasValue)
            {
                property.HasMaxLength(typeScaffoldingInfo.ScaffoldMaxLength.Value);
            }

            if (column.ValueGenerated == ValueGenerated.OnAdd)
            {
                property.ValueGeneratedOnAdd();
            }

            if (column.ValueGenerated == ValueGenerated.OnUpdate)
            {
                property.ValueGeneratedOnUpdate();
            }

            if (column.ValueGenerated == ValueGenerated.OnAddOrUpdate)
            {
                property.ValueGeneratedOnAddOrUpdate();
            }

            if (column.DefaultValue != null)
            {
                property.HasDefaultValueSql(column.DefaultValue);
            }

            if (column.ComputedValue != null)
            {
                property.HasComputedColumnSql(column.ComputedValue);
            }

            if (!column.PrimaryKeyOrdinal.HasValue)
            {
                property.IsRequired(!column.IsNullable && !forceNullable);
            }

            property.Metadata.Scaffolding().ColumnOrdinal = column.Ordinal;

            property.Metadata.AddAnnotations(column.GetAnnotations());

            return property;
        }

        protected virtual KeyBuilder VisitPrimaryKey([NotNull] EntityTypeBuilder builder, [NotNull] TableModel table)
        {
            Check.NotNull(builder, nameof(builder));
            Check.NotNull(table, nameof(table));

            var keyColumns = table.Columns
                .Where(c => c.PrimaryKeyOrdinal.HasValue)
                .OrderBy(c => c.PrimaryKeyOrdinal)
                .ToList();

            if (keyColumns.Count == 0)
            {
                Logger.MissingPrimaryKeyWarning(table.DisplayName);
                return null;
            }

            var unmappedColumns = keyColumns
                .Where(c => _unmappedColumns.Contains(c))
                .Select(c => c.Name)
                .ToList();
            if (unmappedColumns.Any())
            {
                Logger.PrimaryKeyColumnsNotMappedWarning(table.DisplayName, unmappedColumns);
                return null;
            }

            var keyBuilder = builder.HasKey(keyColumns.Select(GetPropertyName).ToArray());

            var pkColumns = table.Columns.Where(c => c.PrimaryKeyOrdinal.HasValue).ToList();
            if (pkColumns.Count == 1
                && pkColumns[0].ValueGenerated == null
                && pkColumns[0].DefaultValue == null)
            {
                var property = builder.Metadata.FindProperty(GetPropertyName(pkColumns[0]))?.AsProperty();
                if (property != null)
                {
                    var conventionalValueGenerated = new RelationalValueGeneratorConvention().GetValueGenerated(property);
                    if (conventionalValueGenerated == ValueGenerated.OnAdd)
                    {
                        property.ValueGenerated = ValueGenerated.Never;
                    }
                }
            }

            return keyBuilder;
        }

        protected virtual EntityTypeBuilder VisitIndexes([NotNull] EntityTypeBuilder builder, [NotNull] ICollection<IndexModel> indexes)
        {
            Check.NotNull(builder, nameof(builder));
            Check.NotNull(indexes, nameof(indexes));

            foreach (var index in indexes)
            {
                VisitIndex(builder, index);
            }

            return builder;
        }

        protected virtual IndexBuilder VisitIndex([NotNull] EntityTypeBuilder builder, [NotNull] IndexModel index)
        {
            Check.NotNull(builder, nameof(builder));
            Check.NotNull(index, nameof(index));

            var indexColumns = index.IndexColumns
                .OrderBy(ic => ic.Ordinal)
                .Select(ic => ic.Column)
                .ToList();
            var unmappedColumns = indexColumns
                .Where(c => _unmappedColumns.Contains(c))
                .Select(c => c.Name)
                .ToList();
            if (unmappedColumns.Any())
            {
                Logger.IndexColumnsNotMappedWarning(index.Name, unmappedColumns);
                return null;
            }

            var columnNames = indexColumns.Select(c => c.Name);
            var propertyNames = indexColumns.Select(GetPropertyName).ToArray();
            if (index.Table != null)
            {
                var primaryKeyColumns = index.Table.Columns
                    .Where(c => c.PrimaryKeyOrdinal.HasValue)
                    .OrderBy(c => c.PrimaryKeyOrdinal);
                if (columnNames.SequenceEqual(primaryKeyColumns.Select(c => c.Name))
                    && index.Filter == null)
                {
                    // index is supporting the primary key. So there is no need for
                    // an extra index in the model. But if the index name does not
                    // match what would be produced by default then need to call
                    // HasName() on the primary key.
                    var key = builder.Metadata.FindPrimaryKey();

                    if (index.Name != ConstraintNamer.GetDefaultName(key))
                    {
                        builder.HasKey(propertyNames).HasName(index.Name);
                    }
                    return null;
                }
            }

            var indexBuilder = builder.HasIndex(propertyNames)
                .IsUnique(index.IsUnique);

            if (index.Filter != null)
            {
                indexBuilder.HasFilter(index.Filter);
            }

            if (!string.IsNullOrEmpty(index.Name))
            {
                indexBuilder.HasName(index.Name);
            }

            indexBuilder.Metadata.AddAnnotations(index.GetAnnotations());

            return indexBuilder;
        }

        protected virtual ModelBuilder VisitForeignKeys([NotNull] ModelBuilder modelBuilder, [NotNull] IList<ForeignKeyModel> foreignKeys)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));
            Check.NotNull(foreignKeys, nameof(foreignKeys));

            foreach (var fk in foreignKeys)
            {
                VisitForeignKey(modelBuilder, fk);
            }

            // Note: must completely assign all foreign keys before assigning
            // navigation properties otherwise naming of navigation properties
            // when there are multiple foreign keys does not work.
            foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(et => et.GetForeignKeys()))
            {
                AddNavigationProperties(foreignKey);
            }

            return modelBuilder;
        }

        protected virtual IMutableForeignKey VisitForeignKey([NotNull] ModelBuilder modelBuilder, [NotNull] ForeignKeyModel foreignKey)
        {
            Check.NotNull(modelBuilder, nameof(modelBuilder));
            Check.NotNull(foreignKey, nameof(foreignKey));

            if (foreignKey.PrincipalTable == null)
            {
                Logger.ForeignKeyReferencesMissingTableWarning(foreignKey.DisplayName);
                return null;
            }

            if (foreignKey.Table == null)
            {
                return null;
            }

            var dependentEntityType = modelBuilder.Model.FindEntityType(GetEntityTypeName(foreignKey.Table));

            if (dependentEntityType == null)
            {
                return null;
            }

            var foreignKeyColumns = foreignKey.Columns.OrderBy(fc => fc.Ordinal);
            var unmappedDependentColumns = foreignKeyColumns
                .Select(fc => fc.Column)
                .Where(c => _unmappedColumns.Contains(c))
                .Select(c => c.Name)
                .ToList();
            if (unmappedDependentColumns.Any())
            {
                Logger.ForeignKeyColumnsNotMappedWarning(foreignKey.DisplayName, unmappedDependentColumns);
                return null;
            }

            var dependentProperties = foreignKeyColumns
                .Select(fc => GetPropertyName(fc.Column))
                .Select(name => dependentEntityType.FindProperty(name))
                .ToList()
                .AsReadOnly();

            var principalEntityType = modelBuilder.Model.FindEntityType(GetEntityTypeName(foreignKey.PrincipalTable));
            if (principalEntityType == null)
            {
                Logger.ForeignKeyReferencesNotMappedTableWarning(foreignKey.DisplayName, foreignKey.PrincipalTable.DisplayName);
                return null;
            }

            var unmappedPrincipalColumns = foreignKeyColumns
                .Select(fc => fc.PrincipalColumn)
                .Where(pc => principalEntityType.FindProperty(GetPropertyName(pc)) == null)
                .Select(pc => pc.Name)
                .ToList();
            if (unmappedPrincipalColumns.Any())
            {
                Logger.ForeignKeyColumnsNotMappedWarning(foreignKey.DisplayName, unmappedPrincipalColumns);
                return null;
            }

            var principalPropertiesMap = foreignKeyColumns
                .Select(
                    fc => new Tuple<IMutableProperty, ColumnModel>(
                        principalEntityType.FindProperty(GetPropertyName(fc.PrincipalColumn)),
                        fc.PrincipalColumn));
            var principalProperties = principalPropertiesMap
                .Select(tuple => tuple.Item1)
                .ToList();

            var principalKey = principalEntityType.FindKey(principalProperties);
            if (principalKey == null)
            {
                var index = principalEntityType.FindIndex(principalProperties.AsReadOnly());
                if (index != null
                    && index.IsUnique)
                {
                    // ensure all principal properties are non-nullable even if the columns
                    // are nullable on the database. EF's concept of a key requires this.
                    var nullablePrincipalProperties =
                        principalPropertiesMap.Where(tuple => tuple.Item1.IsNullable);
                    if (nullablePrincipalProperties.Any())
                    {
                        Logger.ForeignKeyPrincipalEndContainsNullableColumnsWarning(
                            foreignKey.DisplayName,
                            index.Relational().Name,
                            nullablePrincipalProperties.Select(tuple => tuple.Item2.DisplayName).ToList());

                        nullablePrincipalProperties
                            .ToList()
                            .ForEach(tuple => tuple.Item1.IsNullable = false);
                    }
                    principalKey = principalEntityType.AddKey(principalProperties);
                }
                else
                {
                    var principalColumns = foreignKeyColumns.Select(c => c.PrincipalColumn.Name).ToList();

                    Logger.ForeignKeyReferencesMissingPrincipalKeyWarning(
                        foreignKey.DisplayName, principalEntityType.DisplayName(), principalColumns);

                    return null;
                }
            }

            var key = dependentEntityType.GetOrAddForeignKey(
                dependentProperties, principalKey, principalEntityType);

            var dependentKey = dependentEntityType.FindKey(dependentProperties);
            var dependentIndex = dependentEntityType.FindIndex(dependentProperties);
            key.IsUnique = dependentKey != null
                           || (dependentIndex != null && dependentIndex.IsUnique);

            key.Relational().Name = foreignKey.Name;

            AssignOnDeleteAction(foreignKey, key);

            key.AddAnnotations(foreignKey.GetAnnotations());

            return key;
        }

        protected virtual void AddNavigationProperties([NotNull] IMutableForeignKey foreignKey)
        {
            Check.NotNull(foreignKey, nameof(foreignKey));

            var dependentEndExistingIdentifiers = ExistingIdentifiers(foreignKey.DeclaringEntityType);
            var dependentEndNavigationPropertyCandidateName =
                CandidateNamingService.GetDependentEndCandidateNavigationPropertyName(foreignKey);
            var dependentEndNavigationPropertyName =
                _cSharpUtilities.GenerateCSharpIdentifier(
                    dependentEndNavigationPropertyCandidateName,
                    dependentEndExistingIdentifiers,
                    NavigationUniquifier);

            foreignKey.HasDependentToPrincipal(dependentEndNavigationPropertyName);

            var principalEndExistingIdentifiers = ExistingIdentifiers(foreignKey.PrincipalEntityType);
            var principalEndNavigationPropertyCandidateName = foreignKey.IsSelfReferencing()
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    SelfReferencingPrincipalEndNavigationNamePattern,
                    dependentEndNavigationPropertyName)
                : CandidateNamingService.GetPrincipalEndCandidateNavigationPropertyName(
                    foreignKey, dependentEndNavigationPropertyName);

            if (!foreignKey.IsUnique
                && !foreignKey.IsSelfReferencing())
            {
                principalEndNavigationPropertyCandidateName = _pluralizer.Pluralize(principalEndNavigationPropertyCandidateName);
            }

            var principalEndNavigationPropertyName =
                _cSharpUtilities.GenerateCSharpIdentifier(
                    principalEndNavigationPropertyCandidateName,
                    principalEndExistingIdentifiers,
                    NavigationUniquifier);

            foreignKey.HasPrincipalToDependent(principalEndNavigationPropertyName);
        }

        // Stores the names of the EntityType itself and its Properties, but does not include any Navigation Properties
        private readonly Dictionary<IEntityType, List<string>> _entityTypeAndPropertyIdentifiers = new Dictionary<IEntityType, List<string>>();

        protected virtual List<string> ExistingIdentifiers([NotNull] IEntityType entityType)
        {
            Check.NotNull(entityType, nameof(entityType));

            List<string> existingIdentifiers;
            if (!_entityTypeAndPropertyIdentifiers.TryGetValue(entityType, out existingIdentifiers))
            {
                existingIdentifiers = new List<string>();
                existingIdentifiers.Add(entityType.Name);
                existingIdentifiers.AddRange(entityType.GetProperties().Select(p => p.Name));
                _entityTypeAndPropertyIdentifiers[entityType] = existingIdentifiers;
            }

            existingIdentifiers.AddRange(entityType.GetNavigations().Select(p => p.Name));
            return existingIdentifiers;
        }

        protected virtual TypeScaffoldingInfo GetTypeScaffoldingInfo([NotNull] ColumnModel columnModel)
        {
            if (columnModel.StoreType == null)
            {
                return null;
            }

            var typeScaffoldingInfo = _scaffoldingTypeMapper.FindMapping(
                columnModel.UnderlyingStoreType ?? columnModel.StoreType,
                keyOrIndex: false,
                rowVersion: false);
            if (columnModel.UnderlyingStoreType != null)
            {
                return new TypeScaffoldingInfo(
                    typeScaffoldingInfo.ClrType,
                    inferred: false,
                    scaffoldUnicode: typeScaffoldingInfo.ScaffoldUnicode,
                    scaffoldMaxLength: typeScaffoldingInfo.ScaffoldMaxLength);
            }

            return typeScaffoldingInfo;
        }

        private static void AssignOnDeleteAction(
            [NotNull] ForeignKeyModel fkModel, [NotNull] IMutableForeignKey foreignKey)
        {
            Check.NotNull(fkModel, nameof(fkModel));
            Check.NotNull(foreignKey, nameof(foreignKey));

            switch (fkModel.OnDelete)
            {
                case ReferentialAction.Cascade:
                    foreignKey.DeleteBehavior = DeleteBehavior.Cascade;
                    break;

                case ReferentialAction.SetNull:
                    foreignKey.DeleteBehavior = DeleteBehavior.SetNull;
                    break;

                default:
                    foreignKey.DeleteBehavior = DeleteBehavior.ClientSetNull;
                    break;
            }
        }

        // TODO use CSharpUniqueNamer
        private string NavigationUniquifier([NotNull] string proposedIdentifier, [CanBeNull] ICollection<string> existingIdentifiers)
        {
            if (existingIdentifiers == null
                || !existingIdentifiers.Contains(proposedIdentifier))
            {
                return proposedIdentifier;
            }

            var finalIdentifier =
                string.Format(CultureInfo.CurrentCulture, NavigationNameUniquifyingPattern, proposedIdentifier);
            var suffix = 1;
            while (existingIdentifiers.Contains(finalIdentifier))
            {
                finalIdentifier = proposedIdentifier + suffix;
                suffix++;
            }

            return finalIdentifier;
        }
    }
}
