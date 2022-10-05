/*
   Copyright 2022 Nora Beda

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DockerTest
{
    public interface IContextBuilder
    {
        public void AddDirectory(string realPath, string virtualPath, bool recurse = true);
        public Task AddDirectoryAsync(string realPath, string virtualPath, bool recurse = true);

        public void AddFile(string realPath, string virtualPath);
        public Task AddFileAsync(string realPath, string virtualPath);

        public void AddEntry(string virtualPath, byte[] contents);
        public Task AddEntryAsync(string virtualPath, byte[] contents);
    }

    public sealed class BuildContext : IDisposable
    {
        private sealed class Builder : IContextBuilder, IDisposable
        {
            public Builder(BuildContext context)
            {
                mContext = context;
                mDisposed = false;

                mGZipStream = new GZipOutputStream(mContext.mStream)
                {
                    IsStreamOwner = false
                };

                mTarStream = new TarOutputStream(mGZipStream, Encoding.UTF8)
                {
                    IsStreamOwner = false
                };
            }

            public void AddDirectory(string realPath, string virtualPath, bool recurse)
            {
                VerifyUsable();

                var files = Directory.GetFiles(realPath);
                foreach (var filePath in files)
                {
                    var filename = Path.GetFileName(filePath);
                    if (filename == null)
                    {
                        continue;
                    }

                    var virtualFilePath = Path.Join(virtualPath, filename);
                    AddFile(filePath, virtualFilePath);
                }

                if (recurse)
                {
                    var directories = Directory.GetDirectories(realPath);
                    foreach (var directory in directories)
                    {
                        var name = Path.GetRelativePath(directory, realPath);
                        var virtualDirectory = Path.Join(virtualPath, name);

                        AddDirectory(directory, virtualDirectory, true);
                    }
                }
            }

            public async Task AddDirectoryAsync(string realPath, string virtualPath, bool recurse)
            {
                VerifyUsable();

                var files = Directory.GetFiles(realPath);
                foreach (var filePath in files)
                {
                    var filename = Path.GetFileName(filePath);
                    if (filename == null)
                    {
                        continue;
                    }

                    var virtualFilePath = Path.Join(virtualPath, filename);
                    await AddFileAsync(filePath, virtualFilePath);
                }

                if (recurse)
                {
                    var directories = Directory.GetDirectories(realPath);
                    foreach (var directory in directories)
                    {
                        var name = Path.GetRelativePath(realPath, directory);
                        var virtualDirectory = Path.Join(virtualPath, name);

                        await AddDirectoryAsync(directory, virtualDirectory, true);
                    }
                }
            }

            public void AddFile(string realPath, string virtualPath)
            {
                VerifyUsable();

                var entry = TarEntry.CreateTarEntry(virtualPath);
                using var stream = new FileStream(realPath, FileMode.Open);

                entry.Size = stream.Length;
                mTarStream.PutNextEntry(entry);

                var buffer = new byte[1024];
                while (true)
                {
                    int numRead = stream.Read(buffer, 0, buffer.Length);
                    if (numRead <= 0)
                    {
                        break;
                    }

                    mTarStream.Write(buffer, 0, numRead);
                }

                mTarStream.CloseEntry();
            }

            public async Task AddFileAsync(string realPath, string virtualPath)
            {
                VerifyUsable();

                using var stream = new FileStream(realPath, FileMode.Open, FileAccess.Read);
                var entry = TarEntry.CreateTarEntry(virtualPath);
                entry.Size = stream.Length;

                await mTarStream.PutNextEntryAsync(entry, default);

                var buffer = new byte[1024];
                while (true)
                {
                    int numRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (numRead <= 0)
                    {
                        break;
                    }

                    await mTarStream.WriteAsync(buffer, 0, numRead, default);
                }

                await mTarStream.CloseEntryAsync(default);
            }

            public void AddEntry(string virtualPath, byte[] contents)
            {
                VerifyUsable();

                var entry = TarEntry.CreateTarEntry(virtualPath);
                entry.Size = contents.Length;

                mTarStream.PutNextEntry(entry);
                mTarStream.Write(contents, 0, contents.Length);
                mTarStream.Close();
            }

            public async Task AddEntryAsync(string virtualPath, byte[] contents)
            {
                VerifyUsable();

                var entry = TarEntry.CreateTarEntry(virtualPath);
                entry.Size = contents.Length;

                var token = new CancellationToken();
                await mTarStream.PutNextEntryAsync(entry, token);

                await mTarStream.WriteAsync(contents, 0, contents.Length, token);
                await mTarStream.CloseEntryAsync(token);
            }

            private void VerifyUsable()
            {
                if (mDisposed)
                {
                    var type = GetType();
                    throw new ObjectDisposedException(type.FullName ?? type.Name);
                }
            }

            public void Dispose()
            {
                if (mDisposed)
                {
                    return;
                }

                mTarStream.Dispose();
                mGZipStream.Dispose();

                mDisposed = true;
            }

            private readonly GZipOutputStream mGZipStream;
            private readonly TarOutputStream mTarStream;

            public bool mDisposed;
            private readonly BuildContext mContext;
        }

        public BuildContext()
        {
            mDisposed = false;
            mStream = new MemoryStream();
        }

        public void Rebuild(Action<IContextBuilder> callback)
        {
            VerifyUsable();

            lock (mStream)
            {
                mStream.Position = 0;

                using var builder = new Builder(this);
                callback(builder);
            }
        }

        public async Task RebuildAsync(Func<IContextBuilder, Task> callback)
        {
            VerifyUsable();

            await Task.Run(() =>
            {
                lock (mStream)
                {
                    mStream.Position = 0;

                    using var builder = new Builder(this);
                    callback(builder).Wait();
                }
            });
        }

        public void GetStream(Action<Stream> callback)
        {
            VerifyUsable();

            lock (mStream)
            {
                mStream.Position = 0;
                callback(mStream);
            }
        }

        public async Task GetStreamAsync(Func<Stream, Task> callback)
        {
            VerifyUsable();

            await Task.Run(() =>
            {
                lock (mStream)
                {
                    mStream.Position = 0;
                    callback(mStream).Wait();
                }
            });
        }

        private void VerifyUsable()
        {
            if (mDisposed)
            {
                var type = GetType();
                throw new ObjectDisposedException(type.FullName ?? type.Name);
            }
        }

        public void Dispose()
        {
            if (mDisposed)
            {
                return;
            }

            VerifyUsable();
            mStream.Dispose();

            mDisposed = true;
        }

        private bool mDisposed;
        private readonly MemoryStream mStream;
    }
}