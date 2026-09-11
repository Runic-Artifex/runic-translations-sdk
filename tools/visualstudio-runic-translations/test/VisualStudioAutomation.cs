using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public static class VisualStudioAutomation
{
    [DllImport("ole32.dll")] private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);
    [DllImport("ole32.dll")] private static extern int CreateBindCtx(int reserved, out IBindCtx context);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
    public static IntPtr FindTitle(string title) { return FindWindow(null, title); }
    public static object FindDte(int processId)
    {
        IRunningObjectTable table; IBindCtx context;
        Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out table));
        Marshal.ThrowExceptionForHR(CreateBindCtx(0, out context));
        IEnumMoniker enumerator; table.EnumRunning(out enumerator);
        try
        {
            var monikers = new IMoniker[1];
            while (enumerator.Next(1, monikers, IntPtr.Zero) == 0)
            {
                string name; monikers[0].GetDisplayName(context, null, out name);
                if (name.StartsWith("!VisualStudio.DTE.", StringComparison.Ordinal) && name.EndsWith(":" + processId, StringComparison.Ordinal))
                { object value; table.GetObject(monikers[0], out value); return value; }
            }
            return null;
        }
        finally { Marshal.ReleaseComObject(enumerator); Marshal.ReleaseComObject(context); Marshal.ReleaseComObject(table); }
    }
}
