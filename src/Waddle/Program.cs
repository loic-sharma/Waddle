using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Waddle
{
    public class Program
    {
        public static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        public static async Task MainAsync(string[] args)
        {
            var sourceText = SourceText.From(Program1);
            var cancellationToken = CancellationToken.None;
            var workspace = new AdhocWorkspace();

            var projectInfo = ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "HelloWorld",
                "HelloWorld",
                LanguageNames.CSharp,
                metadataReferences: new List<MetadataReference>
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
                });

            var project = workspace.AddProject(projectInfo);
            var document = workspace.AddDocument(project.Id, "Program.cs", sourceText);

            var context = new Context(workspace);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            //ModifyWorkspace(workspace, document);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            await context.RunAsync(cancellationToken);
        }

        private static async Task ModifyWorkspace(Workspace workspace, Document document)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));

            var newSourceText = SourceText.From(Program2);
            var newSolution = workspace.CurrentSolution.WithDocumentText(document.Id, newSourceText);

            workspace.TryApplyChanges(newSolution);
        }

        public static Stream Program1
            => File.OpenRead(Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Program1.txt"));

        public static Stream Program2
            => File.OpenRead(Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "Program2.txt"));
    }
}
