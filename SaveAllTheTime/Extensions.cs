using System.Collections.Generic;
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
    }
}
