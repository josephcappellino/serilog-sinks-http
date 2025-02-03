﻿// Copyright 2015-2025 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;
using Serilog.Parsing;

namespace Serilog.Sinks.Http.TextFormatters;

/// <summary>
/// JSON formatter serializing log events into a normal format with its data normalized. The
/// lack of a rendered message means improved network load compared to
/// <see cref="NormalRenderedTextFormatter"/>. Often this formatter is complemented with a log
/// server that is capable of rendering the messages of the incoming log events.
/// </summary>
/// <seealso cref="NormalRenderedTextFormatter" />
/// <seealso cref="CompactTextFormatter" />
/// <seealso cref="CompactRenderedTextFormatter" />
/// <seealso cref="NamespacedTextFormatter" />
/// <seealso cref="ITextFormatter" />
public class NormalTextFormatter : ITextFormatter
{
    /// <summary>
    /// Gets or sets a value indicating whether the message is rendered into JSON.
    /// </summary>
    protected bool IsRenderingMessage { get; set; }

    /// <summary>
    /// Used to determine if any items have been added to the JSON, yet.
    /// </summary>
    private bool hasTags = false;

    /// <summary>
    /// Writes the tag and value to the output.
    /// </summary>
    /// <param name="tag">The JSON Tag.</param>
    /// <param name="value">The Tag's value.</param>
    /// <param name="output">The output.</param>
    protected void Write(string tag, string value, TextWriter output)
    {
        if (hasTags)
        {
            output.Write(',');
        }

        JsonValueFormatter.WriteQuotedJsonString(tag, output);
        output.Write(":");
        JsonValueFormatter.WriteQuotedJsonString(value, output);

        hasTags = true;
    }

    /// <summary>
    /// Format the log event into the output.
    /// </summary>
    /// <param name="logEvent">The event to format.</param>
    /// <param name="output">The output.</param>
    public void Format(LogEvent logEvent, TextWriter output)
    {
        try
        {
            hasTags = false; // force reset

            var buffer = new StringWriter();
            FormatContent(logEvent, buffer);

            // If formatting was successful, write to output
            output.WriteLine(buffer.ToString());
        }
        catch (Exception e)
        {
            LogNonFormattableEvent(logEvent, e);
        }
    }

    private void FormatContent(LogEvent logEvent, TextWriter output)
    {
        if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));
        if (output == null) throw new ArgumentNullException(nameof(output));

        output.Write("{");

        WriteTimestamp(logEvent, output);
        WriteLogLevel(logEvent, output);
        WriteMessageTemplate(logEvent, output);

        if (IsRenderingMessage)
        {
            WriteRenderedMessage(logEvent, output);
        }

        if (logEvent.Exception != null)
        {
            WriteException(logEvent, output);
        }

        if (logEvent.TraceId != null)
        {
            WriteTraceId(logEvent, output);
        }

        if (logEvent.SpanId != null)
        {
            WriteSpanId(logEvent, output);
        }

        if (logEvent.Properties.Count != 0)
        {
            WriteProperties(logEvent.Properties, output);
        }

        // Better not to allocate an array in the 99.9% of cases where this is false
        var tokensWithFormat = logEvent.MessageTemplate.Tokens
            .OfType<PropertyToken>()
            .Where(pt => pt.Format != null);

        // ReSharper disable once PossibleMultipleEnumeration
        if (tokensWithFormat.Any())
        {
            // ReSharper disable once PossibleMultipleEnumeration
            WriteRenderings(tokensWithFormat.GroupBy(pt => pt.PropertyName), logEvent.Properties, output);
        }

        output.Write('}');
    }

    /// <summary>
    /// Writes the timestamp in UTC format to the output.
    /// </summary>
    /// <param name="logEvent">The event to format.</param>
    /// <param name="output">The output.</param>
    protected virtual void WriteTimestamp(LogEvent logEvent, TextWriter output) =>
        Write("Timestamp", logEvent.Timestamp.UtcDateTime.ToString("O"), output);

    /// <summary>
    /// Writes the log level to the output.
    /// </summary>
    /// <param name="logEvent">The event to format.</param>
    /// <param name="output">The output.</param>
    protected virtual void WriteLogLevel(LogEvent logEvent, TextWriter output) =>
        Write("Level", logEvent.Level.ToString(), output);

    /// <summary>
    /// Writes the message template to the output.
    /// </summary>
    /// <param name="logEvent">The event to format.</param>
    /// <param name="output">The output.</param>
    protected virtual void WriteMessageTemplate(LogEvent logEvent, TextWriter output) =>
        Write("MessageTemplate", logEvent.MessageTemplate.Text, output);

    /// <summary>
    /// Writes the rendered message to the output.
    /// </summary>
    /// <param name="logEvent">The event to format.</param>
    /// <param name="output">The output.</param>
    protected virtual void WriteRenderedMessage(LogEvent logEvent, TextWriter output) =>
        Write("RenderedMessage", logEvent.MessageTemplate.Render(logEvent.Properties), output);

    /// <summary>
    /// Writes the exception to the output.
    /// </summary>
    /// <param name="logEvent">The event to format.</param>
    /// <param name="output">The output.</param>
    protected virtual void WriteException(LogEvent logEvent, TextWriter output) =>
        Write("Exception", logEvent.Exception?.ToString() ?? "", output);

    /// <summary>
    /// Writes the Trace ID to the output.
    /// </summary>
    /// <param name="logEvent">The event to format.</param>
    /// <param name="output">The output.</param>
    protected virtual void WriteTraceId(LogEvent logEvent, TextWriter output) =>
        Write("TraceId", logEvent.TraceId?.ToString() ?? "", output);

    /// <summary>
    /// Writes the Span ID to the output.
    /// </summary>
    /// <param name="logEvent">The event to format.</param>
    /// <param name="output">The output.</param>
    protected virtual void WriteSpanId(LogEvent logEvent, TextWriter output) =>
        Write("SpanId", logEvent.SpanId?.ToString() ?? "", output);

    /// <summary>
    /// Writes the collection of properties to the output.
    /// </summary>
    /// <param name="properties">The collection of log properties.</param>
    /// <param name="output">The output.</param>
    protected virtual void WriteProperties(
        IReadOnlyDictionary<string, LogEventPropertyValue> properties,
        TextWriter output)
    {
        output.Write(",\"Properties\":{");

        var precedingDelimiter = string.Empty;

        foreach (var property in properties)
        {
            output.Write(precedingDelimiter);
            precedingDelimiter = ",";

            JsonValueFormatter.WriteQuotedJsonString(property.Key, output);
            output.Write(':');
            ValueFormatter.Instance.Format(property.Value, output);
        }

        output.Write('}');
    }

    /// <summary>
    /// Writes the items with rendering formats to the output.
    /// </summary>
    /// <param name="tokensWithFormat">The collection of tokens that have formats.</param>
    /// <param name="properties">The collection of properties to fill the tokens.</param>
    /// <param name="output">The output.</param>
    protected virtual void WriteRenderings(
        IEnumerable<IGrouping<string, PropertyToken>> tokensWithFormat,
        IReadOnlyDictionary<string, LogEventPropertyValue> properties,
        TextWriter output)
    {
        output.Write(",\"Renderings\":{");

        var rdelim = string.Empty;
        foreach (var ptoken in tokensWithFormat)
        {
            output.Write(rdelim);
            rdelim = ",";

            JsonValueFormatter.WriteQuotedJsonString(ptoken.Key, output);
            output.Write(":[");

            var fdelim = string.Empty;
            foreach (var format in ptoken)
            {
                output.Write(fdelim);
                fdelim = ",";

                output.Write("{\"Format\":");
                JsonValueFormatter.WriteQuotedJsonString(format.Format ?? "\"\"", output);

                output.Write(",\"Rendering\":");
                var sw = new StringWriter();
                format.Render(properties, sw);
                JsonValueFormatter.WriteQuotedJsonString(sw.ToString(), output);
                output.Write('}');
            }

            output.Write(']');
        }

        output.Write('}');
    }

    private static void LogNonFormattableEvent(LogEvent logEvent, Exception e)
    {
        SelfLog.WriteLine(
            "Event at {0} with message template {1} could not be formatted into JSON and will be dropped: {2}",
            logEvent.Timestamp.ToString("o"),
            logEvent.MessageTemplate.Text,
            e);
    }
}
