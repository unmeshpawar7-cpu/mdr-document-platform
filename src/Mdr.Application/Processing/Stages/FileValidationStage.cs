using System.Text;
using Mdr.Application.Options;
using Mdr.Domain.Validation;
using Microsoft.Extensions.Options;

namespace Mdr.Application.Processing.Stages;

/// <summary>Stage 1: file-level checks (extension, size, emptiness, UTF-8 encoding, header present).</summary>
public interface IFileValidationStage
{
    /// <summary>Checks that need no I/O; run before the upload body is read.</summary>
    IReadOnlyList<ValidationFinding> ValidateMetadata(string fileName, long lengthBytes);

    /// <summary>Streams the whole content once with a strict UTF-8 decoder (constant memory).</summary>
    Task<IReadOnlyList<ValidationFinding>> ValidateContentAsync(Stream content, CancellationToken cancellationToken);
}

public sealed class FileValidationStage : IFileValidationStage
{
    private const int BufferSize = 64 * 1024;
    private const int MaxHeaderChars = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IOptions<MdrProcessingOptions> _options;

    public FileValidationStage(IOptions<MdrProcessingOptions> options)
    {
        _options = options;
    }

    public IReadOnlyList<ValidationFinding> ValidateMetadata(string fileName, long lengthBytes)
    {
        var findings = new List<ValidationFinding>();
        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (!string.Equals(extension, MdrProcessingOptions.AllowedExtension, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(ValidationFinding.FileLevel(
                ValidationCodes.FileExtension,
                $"Only {MdrProcessingOptions.AllowedExtension} files are accepted.",
                string.IsNullOrEmpty(extension) ? "(none)" : extension));
        }

        if (lengthBytes <= 0)
        {
            findings.Add(ValidationFinding.FileLevel(ValidationCodes.FileEmpty, "The uploaded file is empty."));
        }
        else if (lengthBytes > _options.Value.MaxFileSizeBytes)
        {
            findings.Add(ValidationFinding.FileLevel(
                ValidationCodes.FileTooLarge,
                $"The file is {lengthBytes} bytes; the maximum is {_options.Value.MaxFileSizeBytes} bytes.",
                lengthBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return findings;
    }

    public async Task<IReadOnlyList<ValidationFinding>> ValidateContentAsync(Stream content, CancellationToken cancellationToken)
    {
        var findings = new List<ValidationFinding>();
        var decoder = StrictUtf8.GetDecoder();
        var buffer = new byte[BufferSize];
        var chars = new char[StrictUtf8.GetMaxCharCount(BufferSize)];
        var line = new StringBuilder();
        var headerFound = false;
        var firstChunk = true;
        long total = 0;

        try
        {
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                if (firstChunk)
                {
                    firstChunk = false;
                    if (read >= 2 && ((buffer[0] == 0xFF && buffer[1] == 0xFE) || (buffer[0] == 0xFE && buffer[1] == 0xFF)))
                    {
                        findings.Add(ValidationFinding.FileLevel(
                            ValidationCodes.FileEncoding,
                            "UTF-16/UTF-32 encoded files are not supported. Save the file as 'CSV UTF-8'."));
                        return findings;
                    }
                }

                total += read;
                var charCount = decoder.GetChars(buffer, 0, read, chars, 0, flush: false);
                if (Array.IndexOf(chars, '\0', 0, charCount) >= 0)
                {
                    findings.Add(ValidationFinding.FileLevel(
                        ValidationCodes.FileEncoding,
                        "The file contains NUL characters; it looks like a binary file rather than CSV text."));
                    return findings;
                }

                if (!headerFound)
                {
                    headerFound = ScanForHeader(chars, charCount, line);
                }
            }

            decoder.GetChars(Array.Empty<byte>(), 0, 0, chars, 0, flush: true);
        }
        catch (DecoderFallbackException)
        {
            findings.Add(ValidationFinding.FileLevel(
                ValidationCodes.FileEncoding,
                $"The file is not valid UTF-8 (invalid byte sequence within the first {total} bytes). Save the file as 'CSV UTF-8'."));
            return findings;
        }

        if (total == 0)
        {
            findings.Add(ValidationFinding.FileLevel(ValidationCodes.FileEmpty, "The uploaded file is empty."));
            return findings;
        }

        if (!headerFound && line.ToString().Trim().Trim('﻿').Length == 0)
        {
            findings.Add(ValidationFinding.FileLevel(ValidationCodes.HeaderMissing, "The file contains no header row."));
        }

        return findings;
    }

    /// <summary>Returns true once a non-blank line has been completed (or the line buffer is full).</summary>
    private static bool ScanForHeader(char[] chars, int count, StringBuilder line)
    {
        for (var i = 0; i < count; i++)
        {
            var ch = chars[i];
            if (ch == '\n')
            {
                if (line.ToString().Trim().Trim('﻿').Length > 0)
                {
                    return true;
                }

                line.Clear();
                continue;
            }

            line.Append(ch);
            if (line.Length >= MaxHeaderChars)
            {
                return true;
            }
        }

        return false;
    }
}
