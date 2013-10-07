using System.Collections.Generic;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace SaveAllTheTime
{


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
            HashSet<string> items = new HashSet<string>();

            foreach (string key in solution.Projects.Cast<Project>().SelectMany(x => AllProjectItems(x).Where(y => y.Properties != null).Select(y => y.Properties.Item("FullPath")).Where(z => z.Value != null).Select(z => z.Value.ToString())))
            {
                items.Add(key);
            }

            return items;
        }

        private static IEnumerable<ProjectItem> AllProjectItems(Project project)
        {
            if (project == null || project.ProjectItems == null) {
                yield break;
            }

            foreach (ProjectItem item in project.ProjectItems)
            {
                yield return item;

                foreach (ProjectItem subitem in SubProjectItems(item))
                {
                    yield return subitem;
                }
            }
        }

        private static IEnumerable<ProjectItem> SubProjectItems(ProjectItem item)
        {
            if (item == null || item.ProjectItems == null) {
                yield break;
            }

            foreach (ProjectItem record in item.ProjectItems)
            {
                yield return record;

                foreach (ProjectItem child in SubProjectItems(record))
                {
                    yield return child;
                }
            }
        }
    }
}
