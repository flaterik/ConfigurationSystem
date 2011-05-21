using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace MySpace.Common.Runtime.InteropServices
{
	/// <summary>
    /// ShadowCopier allows the copying of files from an assembly directory to the current shadow directory.
    /// </summary>
    public static class ShadowCopier
    {
        /// <summary>
        ///  Copies a file from an assembly directory to the current shadow directory.
        ///  The assembly directory is the first directory found containing <paramref name="fileName"/> in:
        ///  <list type="number">
        ///  <item><description>The list of directories in <see cref="AppDomain.RelativeSearchPath"/></description></item>
        ///  <item><description>AppDomain.CurrentDomain.BaseDirectory</description></item>
        ///  <item><description>Environment.CurrentDirectory</description></item>
        ///  </list>
        ///  The shadow directory is specified by the <see cref="ShadowDirectory"/> property.
        /// </summary>
        /// <param name="fileName">The file to move from an assembly directory to the shadow directory.
        /// This must be a relative pathname.</param>
        /// <returns><see cref="ShadowCopyStatus"/> which indicates if the copy occurred
        /// or the reason why it didn't.</returns>
        /// <exception cref="System.ArgumentException"><paramref name="fileName"/> is not a relative path, 
        /// is a zero-length string, contains only white space, 
        /// or contains one or more invalid characters as defined by 
        /// System.IO.Path.InvalidPathChars.-or-specifies a directory.</exception>
        /// <exception cref="System.ArgumentNullException"><paramref name="fileName"/> is null.</exception>
        /// <exception cref="System.IO.PathTooLongException"><paramref name="fileName"/> or the destination shadow
        /// path exceeds the system-defined maximum length. For example, on Windows-based platforms, paths must be less than
        /// 248 characters, and file names must be less than 260 characters.</exception>
        /// <exception cref="System.IO.DirectoryNotFoundException">The path specified in <paramref name="fileName"/> 
        /// is invalid (for example, it is on an unmapped drive).</exception>        
        /// <exception cref="System.IO.FileNotFoundException"><paramref name="fileName"/> was not found.</exception>
        /// <exception cref="System.IO.IOException">An I/O error has occurred.</exception>
        /// <exception cref="System.NotSupportedException">The file '<paramref name="fileName"/>' is in an invalid format.</exception>
        static public ShadowCopyStatus CopyAssemblyFile(string fileName)
        {
            if (fileName == null)
                throw new ArgumentNullException("fileName");

            fileName = fileName.Trim();

            if (fileName.Length == 0)
                throw new ArgumentException("Path must not be zero-length or consist only of white space.", "fileName");

            if (Path.IsPathRooted(fileName))
                throw new ArgumentException("Only relative source paths are supported.", "fileName");

            if (!AppDomain.CurrentDomain.ShadowCopyFiles)
                return ShadowCopyStatus.CopyNotRequired;

            return ShadowCopyFiles(GetAssemblyFileSourcePath(fileName),
                                   Path.Combine(ShadowDirectory, fileName),
                                   ShadowDirectory);
        }

        /// <summary>Gets the shadow directory used by <see cref="CopyAssemblyFile"/>.
        /// It may be null if shadow copying is not being used for the current AppDomain.
        /// </summary>
        static public string ShadowDirectory
        {
            get
            {
                return AppDomain.CurrentDomain.DynamicDirectory ??
                       AppDomain.CurrentDomain.SetupInformation.CachePath;
            }
        }

        static private IEnumerable<string> GetAssemblyFileSearchPaths()
        {
            if (AppDomain.CurrentDomain.RelativeSearchPath != null)
            {
                foreach (string dir in AppDomain.CurrentDomain.RelativeSearchPath.Split(';'))
                    yield return dir;
            }

            yield return AppDomain.CurrentDomain.BaseDirectory;
            yield return Environment.CurrentDirectory;
        }

        static private string FindFileInSourcePaths(string path, IEnumerable<string> sourcePaths)
        {
            return sourcePaths.FirstOrDefault(dir =>
            {
                string fullPath = Path.Combine(dir, path);
                return File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.Directory) == 0;
            });
        }

        static private string GetAssemblyFileSourcePath(string path)
        {
            string result = FindFileInSourcePaths(path, GetAssemblyFileSearchPaths());
            if (result == null)
                throw new FileNotFoundException("Native Assembly wasn't found in the current application assembly paths (eg: base directory or /Bin).",
                                                 path);
            return Path.Combine(result, path);
        }

        static private readonly Object _copyLock = new Object();

        static private ShadowCopyStatus ShadowCopyFiles(string sourceFilePath, string destFilePath, string destDirpath)
        {
            ShadowCopyStatus status = ShadowCopyStatus.CopyNotRequired;
            lock (_copyLock)
            {
                if (ShouldCopy(sourceFilePath, destFilePath))
                    status = CreateShadow(sourceFilePath, destFilePath);

                // Even if the shadow copy exists previously, make sure the OS can find native dlls.
                SafeNativeMethods.SetDllDirectory(destDirpath);
            }

            return status;
        }

        static private ShadowCopyStatus CreateShadow(string sourcePath, string destPath)
        {
            ShadowCopyStatus status = ShadowCopyStatus.Updated;
            bool wasReadOnly = false;

            // Allow this to throw if it doesn't succeed.
            Directory.CreateDirectory(Path.GetDirectoryName(destPath));

            try
            {
                wasReadOnly = SetReadOnlyAttribute(false, destPath);

                File.Copy(sourcePath, destPath, true);
            }
            catch (IOException)
            {
                // File Copy errors are OK, because we expect the file to be locked sometimes.
                // IF that's the case, then the next time the application is run, it will succeed 
                // with the new copy.
                status = ShadowCopyStatus.Locked;
            }
            catch (UnauthorizedAccessException)
            {
                status = ShadowCopyStatus.Locked;
            }
            finally
            {
                // If we couldn't update the shadow copy, reset the readonly bit if it was set.
                if (status == ShadowCopyStatus.Locked && wasReadOnly)
                    SetReadOnlyAttribute(true, destPath);
            }

            return status;
        }

        static private bool SetReadOnlyAttribute(bool newState, string path)
        {
            bool oldState = false;

            try
            {
                if (File.Exists(path))
                {
                    FileAttributes attr = File.GetAttributes(path);
                    oldState = (attr & FileAttributes.ReadOnly) != 0;
                    if (oldState != newState)
                    {
                        if (!newState)
                            File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
                        else
                            File.SetAttributes(path, attr | FileAttributes.ReadOnly);
                    }
                }
            }
            catch (IOException) // ignore "in use" exceptions
            {
            }
            catch (UnauthorizedAccessException) // ignore "unauthorized" exceptions
            {
            }

            return oldState;
        }

        static private bool ShouldCopy(string sourcePath, string destPath)
        {
            bool shouldCopy = true;

            if (File.Exists(destPath))
            {
                FileVersionInfo sourceInfo = FileVersionInfo.GetVersionInfo(sourcePath);
                FileVersionInfo destInfo = FileVersionInfo.GetVersionInfo(destPath);
                if (sourceInfo.FileVersion == destInfo.FileVersion)
                    shouldCopy = false;
            }

            return shouldCopy;
        }

        [SuppressUnmanagedCodeSecurity]
        private static class SafeNativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            static public extern bool SetDllDirectory(string lpPathName);
        }
    }
}
