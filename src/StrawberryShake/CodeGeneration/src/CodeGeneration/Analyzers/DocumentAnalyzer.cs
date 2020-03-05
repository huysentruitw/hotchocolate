using System;
using System.Collections.Generic;
using System.Linq;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;
using StrawberryShake.CodeGeneration.Analyzers.Models;
using StrawberryShake.CodeGeneration.Analyzers.Types;
using StrawberryShake.CodeGeneration.Utilities;
using FieldSelection = StrawberryShake.CodeGeneration.Utilities.FieldSelection;

namespace StrawberryShake.CodeGeneration.Analyzers
{
    public class DocumentAnalyzer
    {
        private ISchema? _schema;
        private readonly List<DocumentNode> _documents = new List<DocumentNode>();

        public DocumentAnalyzer SetSchema(ISchema schema)
        {
            return this;
        }

        public DocumentAnalyzer AddDocument(DocumentNode document)
        {
            return this;
        }

        public IDocumentModel Analyze()
        {
            if (_schema is null)
            {
                throw new InvalidOperationException(
                    "You must provide a schema.");
            }

            if (_documents.Count == 0)
            {
                throw new InvalidOperationException(
                    "You must at least provide one document.");
            }

            var types = new List<ITypeModel>();

            CollectEnumTypes(_schema, _documents, types);
            CollectInputObjectTypes(_schema, _documents, types);


            throw new NotImplementedException();
        }

        private void CollectOutputTypes(FieldCollector fieldCollector, DocumentNode document)
        {
            var backlog = new Queue<FieldSelection>();

            foreach (OperationDefinitionNode operation in
                document.Definitions.OfType<OperationDefinitionNode>())
            {
                var root = Path.New(operation.Name!.Value);

                ObjectType operationType = _schema.GetOperationType(operation.Operation);

                ICodeDescriptor resultType =
                    GenerateOperationSelectionSet(
                        fieldCollector, operationType, operation, root, backlog);

                while (backlog.Any())
                {
                    FieldSelection current = backlog.Dequeue();
                    Path path = current.Path.Append(current.ResponseName);

                    if (!current.Field.Type.NamedType().IsLeafType())
                    {
                        GenerateFieldSelectionSet(
                            operation, current.Field.Type,
                            current.Selection, path, backlog);
                    }
                }

                // GenerateResultParserDescriptor(operation, resultType);
            }
        }

        private void GenerateFieldSelectionSet(
            FieldCollector fieldCollector,
            OperationDefinitionNode operation,
            IType fieldType,
            FieldNode fieldSelection,
            Path path,
            Queue<FieldSelection> backlog)
        {
            var namedType = (INamedOutputType)fieldType.NamedType();

            PossibleSelections possibleSelections =
                fieldCollector.CollectFields(
                    namedType,
                    fieldSelection.SelectionSet!,
                    path);

            foreach (SelectionInfo selectionInfo in possibleSelections.Variants)
            {
                EnqueueFields(backlog, selectionInfo.Fields, path);
            }

            if (namedType is UnionType unionType)
            {
                _unionModelGenerator.Generate(
                    _context,
                    operation,
                    unionType,
                    fieldType,
                    fieldSelection,
                    possibleSelections,
                    path);
            }
            else if (namedType is InterfaceType interfaceType)
            {
                _interfaceModelGenerator.Generate(
                    _context,
                    operation,
                    interfaceType,
                    fieldType,
                    fieldSelection,
                    possibleSelections,
                    path);
            }
            else if (namedType is ObjectType objectType)
            {
                _objectModelGenerator.Generate(
                    _context,
                    operation,
                    objectType,
                    fieldType,
                    fieldSelection,
                    possibleSelections,
                    path);
            }
        }

        private static void EnqueueFields(
            Queue<FieldSelection> backlog,
            IEnumerable<FieldSelection> fieldSelections,
            Path path)
        {
            foreach (FieldSelection fieldSelection in fieldSelections)
            {
                backlog.Enqueue(new FieldSelection(
                    fieldSelection.Field,
                    fieldSelection.Selection,
                    path));
            }
        }

        private static void CollectEnumTypes(
            ISchema schema,
            IReadOnlyList<DocumentNode> documents,
            ICollection<ITypeModel> types)
        {
            var analyzer = new EnumTypeUsageAnalyzer(schema);

            foreach (DocumentNode document in documents)
            {
                analyzer.Analyze(document);
            }

            foreach (EnumType enumType in analyzer.EnumTypes)
            {
                RenameDirective? rename;
                var values = new List<EnumValueModel>();

                foreach (EnumValue enumValue in enumType.Values)
                {
                    rename = enumValue.Directives.SingleOrDefault<RenameDirective>();

                    EnumValueDirective? value =
                        enumValue.Directives.SingleOrDefault<EnumValueDirective>();

                    values.Add(new EnumValueModel(
                        Utilities.NameUtils.GetClassName(rename?.Name ?? enumValue.Name),
                        enumValue,
                        enumValue.Description,
                        value?.Value));
                }

                rename = enumType.Directives.SingleOrDefault<RenameDirective>();

                SerializationTypeDirective? serializationType =
                    enumType.Directives.SingleOrDefault<SerializationTypeDirective>();

                types.Add(new EnumTypeModel(
                    Utilities.NameUtils.GetClassName(rename?.Name ?? enumType.Name),
                    enumType.Description,
                    enumType,
                    serializationType?.Name,
                    values));
            }
        }

        private void CollectInputObjectTypes(
            ISchema schema,
            IReadOnlyList<DocumentNode> documents,
            ICollection<ITypeModel> types)
        {
            var analyzer = new InputObjectTypeUsageAnalyzer(schema);

            foreach (DocumentNode document in documents)
            {
                analyzer.Analyze(document);
            }

            foreach (InputObjectType inputObjectType in analyzer.InputObjectTypes)
            {
                RenameDirective? rename;
                var fields = new List<InputFieldModel>();

                foreach (IInputField inputField in inputObjectType.Fields)
                {
                    rename = inputField.Directives.SingleOrDefault<RenameDirective>();

                    fields.Add(new InputFieldModel(
                        Utilities.NameUtils.GetClassName(rename?.Name ?? inputField.Name),
                        inputField.Description,
                        inputField,
                        inputField.Type));
                }

                rename = inputObjectType.Directives.SingleOrDefault<RenameDirective>();

                types.Add(new ComplexInputTypeModel(
                    Utilities.NameUtils.GetClassName(rename?.Name ?? inputObjectType.Name),
                    inputObjectType.Description,
                    inputObjectType,
                    fields));
            }
        }
    }

    internal interface IFoo
    {
        void Generate(
            IModelGeneratorContext context,
            OperationDefinitionNode operation,
            T namedType,
            IType returnType,
            FieldNode fieldSelection,
            PossibleSelections possibleSelections,
            Path path);
    }

    internal interface IModelGeneratorContext
    {
        ISchema Schema { get; }

        IReadOnlyCollection<ITypeModel> Types { get; }

        // IReadOnlyDictionary<FieldNode, string> FieldTypes { get; }

        NameString GetOrCreateName(
            ISyntaxNode node,
            NameString name,
            ISet<string>? skipNames = null);

        /*
        bool TryGetDescriptor<T>(string name, out T? descriptor)
            where T : class, ICodeDescriptor;

        void Register(FieldNode field, ICodeDescriptor descriptor);

        void Register(ICodeDescriptor descriptor, bool update);

        void Register(ICodeDescriptor descriptor);
        */

        void RegisterType(ITypeModel type);


        PossibleSelections CollectFields(
            INamedOutputType type,
            SelectionSetNode selectionSet,
            Path path);
    }
}