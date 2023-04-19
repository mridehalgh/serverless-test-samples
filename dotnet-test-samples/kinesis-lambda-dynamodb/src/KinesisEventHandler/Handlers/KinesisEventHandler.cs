using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.KinesisEvents;
using AWS.Lambda.Powertools.Logging;
using AWS.Lambda.Powertools.Metrics;
using AWS.Lambda.Powertools.Tracing;
using KinesisEventHandler.Infrastructure;
using KinesisEventHandler.Models;

namespace KinesisEventHandler.Handlers;

/// <summary>
/// This class abstracts the AWS interaction between Amazon Kinesis Data Streams (Kinesis) & AWS Lambda Function.
/// </summary>
/// <typeparam name="TMessage">A generic Kinesis Record Model Type</typeparam>
public abstract class KinesisEventHandler<TMessage> where TMessage : class, new()
{
    protected readonly IServiceProvider ServiceProvider;
    private List<KinesisEventResponse.BatchItemFailure> _batchItemFailures;
    private readonly KinesisEventResponse _kinesisEventResponse;

    protected KinesisEventHandler() : this(Startup.ServiceProvider)
    {
        _kinesisEventResponse = new KinesisEventResponse();
    }

    private KinesisEventHandler(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    /// <summary>
    /// This method is completely abstracted from AWS Infrastructure and is called for every message.
    /// </summary>
    /// <param name="message">Kinesis Record Object</param>
    /// <param name="lambdaContext">Lambda Context</param>
    /// <returns></returns>
    public abstract Task ProcessKinesisRecord(TMessage message, ILambdaContext lambdaContext);

    /// <summary>
    /// This method is called for every Lambda invocation. This method takes in a Kinesis event object and creates
    /// an Kinesis Event adapter for processing the shard of Kinesis records.
    /// </summary>
    /// <param name="kinesisEvent">Kinesis Event received by the function handler</param>
    /// <param name="lambdaContext">Lambda Context</param>
    /// <returns></returns>
    [Logging(LogEvent = true, ClearState = true)]
    [Metrics(Namespace = "KinesisEventHandler", CaptureColdStart = true)]
    [Tracing(Namespace = "KinesisEventHandler", SegmentName = "KinesisEventHandler")]
    public async Task<KinesisEventResponse> Handler(KinesisEvent kinesisEvent, ILambdaContext lambdaContext)
    {
        await ProcessEvent(kinesisEvent, lambdaContext);

        // Set BatchItemFailures if any
        if (_batchItemFailures != null)
        {
            _kinesisEventResponse.BatchItemFailures = _batchItemFailures;
        }

        return _kinesisEventResponse;
    }

    /// <summary>
    /// This method abstracts the Kinesis Event for downstream processing.
    /// </summary>
    /// <param name="kinesisEvent">Kinesis Event received by the function handler</param>
    /// <param name="lambdaContext">Lambda Context</param>
    [Tracing(SegmentName = "ProcessEvent")]
    private async Task ProcessEvent(KinesisEvent kinesisEvent, ILambdaContext lambdaContext)
    {
        var kinesisEventRecords = kinesisEvent.Records;
        var batchItemFailures = new List<KinesisEventResponse.BatchItemFailure>();

        foreach (var kinesisRecord in kinesisEventRecords)
        {
            try
            {
                var message = JsonSerializer.Deserialize<TMessage>(kinesisRecord.Kinesis.Data);

                // This abstract method is implemented by the concrete classes i.e. ProcessEmployeeFunction.
                await ProcessKinesisRecord(message, lambdaContext);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                batchItemFailures.Add(
                    new KinesisEventResponse.BatchItemFailure
                    {
                        ItemIdentifier = kinesisRecord.Kinesis.SequenceNumber
                    }
                );
            }
        }

        _batchItemFailures = batchItemFailures;
    }
}