using System.Collections.Generic;
using System.Linq;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace SaveAllTheTime
{
    using System;
    using System.Diagnostics;

    internal static class Extensions
    {
        internal static List<IVsWindowFrame> GetDocumentWindowFrames(this IVsUIShell vsShell)
        {
            IEnumWindowFrames enumFrames;
            var hr = vsShell.GetDocumentWindowEnum(out enumFrames);
            if (ErrorHandler.Failed(hr) || enumFrames == null) {
                return new List<IVsWindowFrame>();
            }

            return enumFrames.GetContents();
        }

        internal static List<IVsWindowFrame> GetContents(this IEnumWindowFrames enumFrames)
        {
            var list = new List<IVsWindowFrame>();
            var array = new IVsWindowFrame[16];

            while (true) {
                uint num;
                var hr = enumFrames.Next((uint)array.Length, array, out num);
                if (ErrorHandler.Failed(hr)) {
                    return new List<IVsWindowFrame>();
                }

                if (0 == num) {
                    return list;
                }

                for (var i = 0; i < num; i++) {
                    list.Add(array[i]);
                }
            }
        }

        internal static HashSet<string> GetProjectItemPaths(this Solution solution)
        {
            var projectItems = solution.AllProjects()
                .SelectMany(x => AllProjectItems(x)
                    .Where(y => y.Properties != null)
                    .Select(y => y.Properties.Item("FullPath"))
                    .Where(z => z.Value != null)
                    .Select(z => z.Value.ToString()));

            return new HashSet<string>(projectItems);
        }

        internal static IEnumerable<Project> AllProjects(this Solution solution)
        {
            var mainProjects = solution.Projects.Cast<Project>();
            var subProjects = solution.Projects.Cast<Project>().Where(x => x.Kind == ProjectKinds.vsProjectKindSolutionFolder).SolutionFolderProjects();

            return mainProjects.Concat(subProjects);
        }

        internal static IEnumerable<Document> AllDocuments(this Documents documents)
        {
            return documents.Cast<Document>().Where(x => !x.Path.StartsWith("vstfs://", StringComparison.InvariantCultureIgnoreCase));
        } 

        static IEnumerable<Project> SolutionFolderProjects(this IEnumerable<Project> projects)
        {
            return projects
                .SelectMany(x => 
                    Enumerable.Range(1, x.ProjectItems.Count)
                        .Select(y => x.ProjectItems.Item(y).SubProject)
                        .Where(p => p != null))
                .SelectMany(x => 
                    x.Kind == ProjectKinds.vsProjectKindSolutionFolder ? 
                        x.AsEnumerable().SolutionFolderProjects() : 
                        x.AsEnumerable()
                );
        }

        static IEnumerable<Project> AsEnumerable(this Project project)
        {
            yield return project;
        }

        static IEnumerable<ProjectItem> AllProjectItems(Project project)
        {
            if (project == null || project.ProjectItems == null) {
                yield break;
            }

            foreach (ProjectItem item in project.ProjectItems) {
                yield return item;

                foreach (ProjectItem subitem in SubProjectItems(item)) {
                    yield return subitem;
                }
            }
        }

        static IEnumerable<ProjectItem> SubProjectItems(ProjectItem item)
        {
            if (item == null || item.ProjectItems == null) {
                yield break;
            }

            foreach (ProjectItem record in item.ProjectItems) {
                yield return record;

                foreach (ProjectItem child in SubProjectItems(record)) {
                    yield return child;
                }
            }
        }
    }
}
