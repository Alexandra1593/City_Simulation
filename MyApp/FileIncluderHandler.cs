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

        public Stream Open(IncludeType type, string fileName, Stream parentStream)
        {
            string filePath = fileName;

            if (!Path.IsPathRooted(filePath))
            {
                string selectedFile = Path.Combine(Environment.CurrentDirectory, fileName);
                if (File.Exists(selectedFile))
                    filePath = selectedFile;
            }

            return new FileStream(filePath, FileMode.Open, FileAccess.Read);
        }

        public void Close(Stream stream)
        {
            stream.Close();
        }
    }
}
