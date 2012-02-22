using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace sh64
{
	class Program
	{
		private const int BufSize = 512;

		static int Main(string[] args)
		{
			//Console.WriteLine("sh64 args: " + string.Join(", ", args));
			if (args.Length == 0)
			{
				Console.Error.WriteLine("Invalid Input");
				return -1;
			}

			var start = new ProcessStartInfo
			{
				FileName = args.First(),
				Arguments = string.Join(" ", args.Skip(1).ToArray()),
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				RedirectStandardInput = true,
				CreateNoWindow = true
			};

			try
			{
				using (var process = Process.Start(start))
				{
					var reading = Task.Factory.StartNew(() =>
						{
							// pipe sh64 input <-> process input
							var input = Console.OpenStandardInput();

							// can only pipe input while we have a live process
							using (var writer = new BinaryWriter(process.StandardInput.BaseStream))
							using (var reader = new BinaryReader(input))
								while (!process.HasExited)
									if (process.StandardInput.BaseStream.CanWrite)
								{
									Drain(reader, false, null, b =>
										{
											writer.Write(b);
											writer.Flush();
										});
								}
						});

					var stdout = new BinaryReader(process.StandardOutput.BaseStream);
					var err = new BinaryReader(process.StandardError.BaseStream);
					while (!process.HasExited)// || HasData(process.StandardError, process.StandardOutput))
					{
						// print std out
						if (process.StandardOutput.BaseStream.CanRead)
							Drain(stdout, false, process.StandardOutput.CurrentEncoding);

						// print std err
						if (process.StandardError.BaseStream.CanRead)
							Drain(err, true, process.StandardError.CurrentEncoding);
					}

					process.WaitForExit();
					reading.Wait();

					return process.ExitCode;
				}
			}
			catch (Win32Exception e) {
				Console.Error.WriteLine(e.ToString());
				return -2;
			}
			catch (FileNotFoundException e) { 
				Console.Error.WriteLine(e.ToString());
				return -3;
			}
		}

		static void Drain(BinaryReader reader, bool error, Encoding enc = null, Action<byte[]> outAction = null)
		{
			if (outAction == null && enc == null) throw new ApplicationException("both out action and enc is null!");
			outAction = outAction ?? (byteArr => Console.Write(enc.GetString(byteArr)));

			var buf = new byte[BufSize];
			//while ((read = reader.Read(buf, 0, buf.Length)) != 0)
				//read = reader.Read(buf, 0, buf.Length);
			int read = -1;
			Func<byte[], int, int, int> toRead = reader.Read;
			while (read != 0)
			{
				var result = toRead.BeginInvoke(buf, 0, buf.Length, null, null);
				result.AsyncWaitHandle.WaitOne(800);
				if (!result.IsCompleted) return;
				if ((read = toRead.EndInvoke(result)) != 0)
				{
					var tmp = new byte[read];
					Array.Copy(buf, tmp, read);
					ColourizeError(error, () => outAction(tmp));
				}
			}
		}

		private static object mutex = new object();
		static void ColourizeError(bool error, Action a)
		{
			lock (mutex)
			{
				var prev = Console.ForegroundColor;
				Console.ForegroundColor = error ? ConsoleColor.Red : prev;
				var mre = new ManualResetEventSlim(false);
				try
				{
					a();
				}
				finally
				{
					Console.ForegroundColor = prev;
					mre.Set(); // runs on GC thread on servers and is reentrant/interleaved concurrency in workstations!
				}
				mre.Wait();
			}
		}
	}
}