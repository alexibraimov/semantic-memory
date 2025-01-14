﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticMemory.Core.Diagnostics;
using Timer = System.Timers.Timer;

namespace Microsoft.SemanticMemory.Core.Pipeline.Queue.FileBasedQueues;

/// <summary>
/// Basic implementation of a file based queue for local testing.
/// This is not meant for production scenarios, only to avoid spinning up additional services.
/// </summary>
public sealed class FileBasedQueue : IQueue
{
    private sealed class MessageEventArgs : EventArgs
    {
        public string Filename { get; set; } = string.Empty;
    }

    /// <summary>
    /// Event triggered when a message is received
    /// </summary>
    private event EventHandler<MessageEventArgs>? Received;

    // Extension of the files containing the messages. Don't leave this empty, it's better
    // filtering and it mitigates the risk of unwanted file deletions.
    private const string FileExt = ".msg";

    // Parent directory of the directory containing messages
    private readonly string _directory;

    // Sorted list of messages (the key is the file path)
    private readonly SortedSet<string> _messages = new();

    // Lock helpers
    private readonly object _lock = new();
    private bool _busy = false;

    // Name of the queue, used also as a directory name
    private string _queueName = string.Empty;

    // Full queue directory path
    private string _queuePath = string.Empty;

    // Timer triggering the filesystem read
    private Timer? _populateTimer;

    // Timer triggering the message dispatch
    private Timer? _dispatchTimer;

    // Application logger
    private readonly ILogger<FileBasedQueue> _log;

    /// <summary>
    /// Create new file based queue
    /// </summary>
    /// <param name="config">File queue configuration</param>
    /// <param name="log">Application logger</param>
    /// <exception cref="InvalidOperationException"></exception>
    public FileBasedQueue(FileBasedQueueConfig config, ILogger<FileBasedQueue>? log = null)
    {
        this._log = log ?? DefaultLogger<FileBasedQueue>.Instance;
        if (!Directory.Exists(config.Path))
        {
            Directory.CreateDirectory(config.Path);
        }

        this._directory = config.Path;
    }

    /// <inherit />
    public Task<IQueue> ConnectToQueueAsync(string queueName, QueueOptions options = default, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(queueName))
        {
            throw new ArgumentOutOfRangeException(nameof(queueName), "The queue name is empty");
        }

        if (!string.IsNullOrEmpty(this._queueName))
        {
            throw new InvalidOperationException($"The queue is already connected to `{this._queueName}`");
        }

        this._queuePath = Path.Join(this._directory, queueName);

        if (!Directory.Exists(this._queuePath))
        {
            Directory.CreateDirectory(this._queuePath);
        }

        this._queueName = queueName;

        if (options.DequeueEnabled)
        {
            this._populateTimer = new Timer(250); // milliseconds
            this._populateTimer.Elapsed += this.PopulateQueue;
            this._populateTimer.Start();

            this._dispatchTimer = new Timer(100); // milliseconds
            this._dispatchTimer.Elapsed += this.DispatchMessages;
            this._dispatchTimer.Start();
        }

        return Task.FromResult<IQueue>(this);
    }

    /// <inherit />
    public async Task EnqueueAsync(string message, CancellationToken cancellationToken = default)
    {
        // Use a sortable file name. Don't use UTC for local development.
        var messageId = DateTimeOffset.Now.ToString("yyyyMMdd.HHmmss.fffffff", CultureInfo.InvariantCulture)
                        + "." + Guid.NewGuid().ToString("N");
        var file = Path.Join(this._queuePath, $"{messageId}{FileExt}");
        await File.WriteAllTextAsync(file, message, cancellationToken).ConfigureAwait(false);

        this._log.LogInformation("Message sent");
    }

    /// <inherit />
    public void OnDequeue(Func<string, Task<bool>> processMessageAction)
    {
        this.Received += async (sender, args) =>
        {
            this._log.LogInformation("Message received");

            string message = await File.ReadAllTextAsync(args.Filename).ConfigureAwait(false);
            bool success = await processMessageAction.Invoke(message).ConfigureAwait(false);
            if (success)
            {
                this.RemoveFileFromQueue(args.Filename);
            }
        };
    }

    /// <inherit />
    public void Dispose()
    {
        this._populateTimer?.Dispose();
        this._dispatchTimer?.Dispose();
    }

    private void RemoveFileFromQueue(string argsFilename)
    {
        if (!File.Exists(argsFilename) || !argsFilename.EndsWith(FileExt, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        this._log.LogTrace("Deleting file from disk {0}", argsFilename);
        File.Delete(argsFilename);

        this._log.LogTrace("Deleting message from memory {0}", argsFilename);
        this._messages.Remove(argsFilename);
    }

    private void PopulateQueue(object? sender, ElapsedEventArgs elapsedEventArgs)
    {
        if (this._busy)
        {
            return;
        }

        lock (this._lock)
        {
            this._busy = true;
            this._log.LogTrace("Populating queue");
            try
            {
                DirectoryInfo d = new(this._queuePath);
                FileInfo[] files = d.GetFiles($"*{FileExt}");
                foreach (FileInfo f in files)
                {
                    this._log.LogInformation("found file {0}", f.FullName);
                    this._messages.Add(f.FullName);
                }
            }
            catch (Exception e)
            {
                this._log.LogError(e, "Fetch failed");
                this._busy = false;
                throw;
            }
        }
    }

    private void DispatchMessages(object? sender, ElapsedEventArgs e)
    {
        if (this._busy || this._messages.Count == 0)
        {
            return;
        }

        lock (this._lock)
        {
            this._busy = true;
            this._log.LogTrace("Dispatching {0} messages", this._messages.Count);
            try
            {
                var messages = this._messages;
                foreach (var filename in messages)
                {
                    this.Received?.Invoke(this, new MessageEventArgs { Filename = filename });
                }
            }
            catch (Exception exception)
            {
                this._log.LogError(exception, "Dispatch failed");
                throw;
            }
            finally
            {
                this._busy = false;
            }
        }
    }
}
