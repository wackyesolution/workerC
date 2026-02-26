using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Domain.Async;
using Core.Autofac.Extension;
using Core.Framework.Extension.Application.Instrumentation.Log;
using Core.Framework.Extension.Cancellation;
using Core.Framework.Extension.Exceptions;
using Core.Framework.Extension.ExternalProcess;
using Core.Framework.Extension.Threading.Asserts;
using cTrader.Automate.BrokerProcess.Json.Contracts;
using cTrader.Automate.Domain.Providers;
using cTrader.Automate.Domain.Shared.Primitives;
using cTrader.Automate.TargetProcess.Native;
using cTrader.Domain.JsonSerialization;

namespace cTrader.Automate.BrokerProcess;

[Export(InstanceKind.PerDependency, new Type[] { typeof(INetCoreBrokerProcess) })]
internal class NetCoreBrokerProcess : INetCoreBrokerProcess, INativeProcess, IDisposable
{
	private const int SuccessResult = 0;

	private static readonly ILogger Logger = LoggerFactory.GetClassLogger("NetCoreBrokerProcess");

	private readonly IAsyncSemaphoreSlim _asyncSemaphoreSlim;

	private readonly Dictionary<long, INativeProcess> _hostProcesses = new Dictionary<long, INativeProcess>();

	private readonly Process _innerProcess;

	private readonly INotIndentedJsonSerializationService _jsonSerializationService;

	private readonly INativeProcess _nativeProcess;

	private readonly INativeProcessProvider _nativeProcessProvider;

	private readonly IPrivateCancellationToken _processCancellationToken;

	private bool _isDisposed;

	private bool _processIsInSuspiciousState;

	public int Id => _nativeProcess.Id;

	public IntPtr Handle => _nativeProcess.Handle;

	public bool HasExited => _nativeProcess.HasExited;

	public int ExitCode => _nativeProcess.ExitCode;

	public event Action<int>? Exited;

	public event Action<TargetId?>? Error;

	public event Action<HostProcessParameters>? HostProcessStarted;

	public NetCoreBrokerProcess(IAutomatePathService automatePathService, INativeProcessProvider nativeProcessProvider, IPrivateCancellationTokenFactory cancellationTokenFactory, IAsyncSemaphoreSlimFactory asyncSemaphoreSlimFactory, INotIndentedJsonSerializationService jsonSerializationService)
	{
		_nativeProcessProvider = nativeProcessProvider;
		_jsonSerializationService = jsonSerializationService;
		_processCancellationToken = cancellationTokenFactory.Create(DummyThreadAssert.Instance);
		_asyncSemaphoreSlim = asyncSemaphoreSlimFactory.Create(1, 1);
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = automatePathService.NetCoreAlgoHostFilePath,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardErrorEncoding = Encoding.Unicode,
			StandardOutputEncoding = Encoding.Unicode
		};
		_innerProcess = new Process
		{
			StartInfo = startInfo,
			EnableRaisingEvents = true
		};
		_innerProcess.Exited += OnInnerProcessOnExited;
		_innerProcess.OutputDataReceived += OnInnerProcessOutputDataReceived;
		_innerProcess.ErrorDataReceived += OnInnerProcessErrorDataReceived;
		try
		{
			_innerProcess.Start();
			_innerProcess.BeginOutputReadLine();
			_innerProcess.BeginErrorReadLine();
		}
		catch (Exception ex)
		{
			Logger.Info(ex);
			throw new InvalidOperationException("Failed to start broker process", ex);
		}
		_nativeProcess = nativeProcessProvider.GetByProcess(_innerProcess);
	}

	public void Kill()
	{
		_nativeProcess.Kill();
	}

	public async Task StartHostProcessAsync(StartHostProcessRequest request)
	{
		if (_isDisposed)
		{
			throw new ObjectDisposedException("NetCoreBrokerProcess");
		}
		if (_nativeProcess.HasExited)
		{
			throw new InvalidOperationException("Broker process exited");
		}
		byte[] buffer = Encoding.Unicode.GetBytes(request.Request);
		using (await _asyncSemaphoreSlim.EnterSemaphoreAsync(_processCancellationToken.PublicToken))
		{
			CancellationTokenSource writeRequestCts = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken, _processCancellationToken.SystemThreadingToken);
			try
			{
				await _innerProcess.StandardInput.BaseStream.WriteAsync(buffer, writeRequestCts.Token);
				await _innerProcess.StandardInput.WriteLineAsync();
				await _innerProcess.StandardInput.FlushAsync();
			}
			catch (IOException ex)
			{
				Logger.Info(ex, "StandardInput stream closed - broker process likely crashed");
				if (_nativeProcess.HasExited)
				{
					throw new InvalidOperationException("Broker process exited");
				}
				throw new InvalidOperationException("Broker process StandardInput is closed", ex);
			}
			finally
			{
				writeRequestCts.Dispose();
			}
		}
	}

	private void OnInnerProcessOutputDataReceived(object sender, DataReceivedEventArgs e)
	{
		string text = e.Data;
		if (string.IsNullOrWhiteSpace(text) && _processIsInSuspiciousState)
		{
			Logger.Error("Twice received empty output");
			return;
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			Logger.Info("Output is empty");
			_processIsInSuspiciousState = true;
			return;
		}
		Logger.Debug(text);
		if (text.StartsWith('\u0a0d'))
		{
			text = text[1..];
		}
		if (!IsJson(text))
		{
			return;
		}
		_processIsInSuspiciousState = false;
		BrokerProcessResponse brokerProcessResponse;
		try
		{
			brokerProcessResponse = _jsonSerializationService.Deserialize<BrokerProcessResponse>(text);
		}
		catch (Exception exception)
		{
			Logger.Info(exception, "Data from broker process: " + text);
			Logger.Error("Exception during deserializing broker process response");
			this.Error?.Invoke(null);
			return;
		}
		int num = (brokerProcessResponse?.Result).NotNull();
		if (num != 0)
		{
			Logger.Info("Data from broker process: " + text);
			Logger.Error(BuildErrorMessage(num));
			if (_hostProcesses.Count == 0)
			{
				_nativeProcess.Kill();
			}
			string value = (brokerProcessResponse?.TargetId).NotNull();
			this.Error?.Invoke(new TargetId(value));
		}
		else
		{
			HandleHostProcessStart(brokerProcessResponse.NotNull());
		}
	}

	private static bool IsJson(string message)
	{
		if (message.StartsWith("{"))
		{
			return message.EndsWith("}");
		}
		return false;
	}

	private void HandleHostProcessStart(BrokerProcessResponse response)
	{
		string value = response.TargetId.NotNull();
		int processId = response.ProcessId;
		Process process = FindProcess(processId);
		if (process == null || process.HasExited)
		{
			Logger.Info($"Process not found or exited. PID: {processId}");
			this.Error?.Invoke(new TargetId(value));
		}
		else
		{
			INativeProcess byProcessId = _nativeProcessProvider.GetByProcessId(processId);
			_hostProcesses.Add(processId, byProcessId);
			byProcessId.Exited += OnHostProcessExited;
			this.HostProcessStarted?.Invoke(new HostProcessParameters(processId, new TargetId(value)));
		}
	}

	private static Process? FindProcess(int processId)
	{
		try
		{
			return Process.GetProcessById(processId);
		}
		catch (Exception exception)
		{
			Logger.Info($"Process not found: {processId}");
			Logger.Info(exception);
		}
		return null;
	}

	private void OnHostProcessExited(int processId)
	{
		INativeProcess nativeProcess = _hostProcesses[processId];
		nativeProcess.Exited -= OnHostProcessExited;
		nativeProcess.Dispose();
		_hostProcesses.Remove(processId);
	}

	private void OnInnerProcessErrorDataReceived(object sender, DataReceivedEventArgs e)
	{
		Logger.Info("Broker process error received " + e.Data);
	}

	private void OnInnerProcessOnExited(object? sender, EventArgs e)
	{
		Logger.Info($"Broker process exit code {_nativeProcess.ExitCode}");
		Dispose();
		this.Exited?.Invoke(Id);
	}

	private static string BuildErrorMessage(int errorCode)
	{
		StringBuilder stringBuilder = new StringBuilder("Error during creating host process");
		try
		{
			Win32Exception ex = new Win32Exception(errorCode);
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(5, 2, stringBuilder2);
			handler.AppendLiteral(" 0x");
			handler.AppendFormatted(errorCode, "X");
			handler.AppendLiteral(": ");
			handler.AppendFormatted(ex.Message);
			stringBuilder2.Append(ref handler);
		}
		catch
		{
		}
		return stringBuilder.ToString();
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}
		_isDisposed = true;
		try
		{
			foreach (var (_, nativeProcess2) in _hostProcesses)
			{
				nativeProcess2.Exited -= OnHostProcessExited;
				nativeProcess2.Dispose();
			}
			_hostProcesses.Clear();
			_innerProcess.Exited -= OnInnerProcessOnExited;
			_innerProcess.OutputDataReceived -= OnInnerProcessOutputDataReceived;
			_innerProcess.ErrorDataReceived -= OnInnerProcessErrorDataReceived;
			try
			{
				_innerProcess.CancelOutputRead();
				_innerProcess.CancelErrorRead();
			}
			catch (InvalidOperationException)
			{
			}
			_nativeProcess.Dispose();
			_asyncSemaphoreSlim.Dispose();
			_processCancellationToken.TryCancel();
		}
		catch (Exception exception)
		{
			Logger.Warn(exception, "[XT-14934] Error during dispose");
		}
	}
}
