using SharpDX;
using SharpDX.D3DCompiler;
using System;
using System.IO;

namespace ProiectSPG
{
    // Required for ShaderBytecode.CompileFromFile API in order to resolve #includes in shader files.
    internal class FileIncludeHandler : CallbackBase, Include
    {
        public static FileIncludeHandler Default { get; } = new FileIncludeHandler();

        //public Stream Open(IncludeType type, string fileName, Stream parentStream)
        //{
        //    string filePath = fileName;

        //    if (!Path.IsPathRooted(filePath))
        //    {
        //        string selectedFile = Path.Combine(Environment.CurrentDirectory, fileName);
        //        if (File.Exists(selectedFile))
        //            filePath = selectedFile;
        //    }

        //    return new FileStream(filePath, FileMode.Open, FileAccess.Read);
        //}

        public Stream Open(IncludeType type, string fileName, Stream parentStream)
        {
            string filePath = fileName;

            if (!Path.IsPathRooted(filePath))
            {
                // 1. try current directory
                string candidate1 = Path.Combine(Environment.CurrentDirectory, fileName);
                if (File.Exists(candidate1))
                    filePath = candidate1;
                else
                {
                    // 2. try Shaders folder under current directory
                    string candidate2 = Path.Combine(Environment.CurrentDirectory, "Shaders", fileName);
                    if (File.Exists(candidate2))
                        filePath = candidate2;
                    else
                    {
                        // 3. try project Shaders folder relative to bin\Debug
                        string candidate3 = Path.GetFullPath(
                            Path.Combine(Environment.CurrentDirectory, @"..\..\Shaders", fileName));

                        if (File.Exists(candidate3))
                            filePath = candidate3;
                        else
                            throw new FileNotFoundException(
                                $"Could not find include file '{fileName}'.\n" +
                                $"Tried:\n{candidate1}\n{candidate2}\n{candidate3}");
                    }
                }
            }

            return new FileStream(filePath, FileMode.Open, FileAccess.Read);
        }

        public void Close(Stream stream)
        {
            stream.Close();
        }
    }
}
