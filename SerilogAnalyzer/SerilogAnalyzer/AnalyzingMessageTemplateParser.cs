﻿// Based on https://github.com/serilog/serilog/blob/e97b3c028bdb28e4430512b84dc2082e6f98dcc7/src/Serilog/Parsing/MessageTemplateParser.cs
// Copyright 2013-2015 Serilog Contributors
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

namespace SerilogAnalyzer
{
    static class AnalyzingMessageTemplateParser
    {
        public static IEnumerable<MessageTemplateDiagnostic> Analyze(string messageTemplate)
        {
            if (messageTemplate == null)
                throw new ArgumentNullException(nameof(messageTemplate));

            if (messageTemplate == "")
                yield break;

            var nextIndex = 0;
            while (true)
            {
                ParseTextToken(nextIndex, messageTemplate, out nextIndex);

                if (nextIndex == messageTemplate.Length)
                    yield break;

                var beforeProp = nextIndex;
                var pt = ParsePropertyToken(nextIndex, messageTemplate, out nextIndex);
                if (beforeProp < nextIndex && pt != null)
                    yield return pt;

                if (nextIndex == messageTemplate.Length)
                    yield break;
            }
        }

        static MessageTemplateDiagnostic ParsePropertyToken(int startAt, string messageTemplate, out int next)
        {
            var first = startAt;
            startAt++;
            while (startAt < messageTemplate.Length && IsValidInPropertyTag(messageTemplate[startAt]))
                startAt++;

            if (startAt == messageTemplate.Length)
            {
                next = startAt;
                return new MessageTemplateDiagnostic(first, next - first, "Encountered end of messageTemplate while parsing property");
            }

            if (messageTemplate[startAt] != '}')
            {
                next = startAt;
                return new MessageTemplateDiagnostic(startAt, 1, "Found invalid character '" + messageTemplate[startAt].ToString() + "' in property");
            }

            next = startAt + 1;

            var rawText = messageTemplate.Substring(first, next - first);
            var tagContent = messageTemplate.Substring(first + 1, next - (first + 2));
            if (tagContent.Length == 0)
                return new MessageTemplateDiagnostic(first, rawText.Length, "Found property without name");

            string propertyNameAndDestructuring, format, alignment;
            MessageTemplateDiagnostic tagContentDiagnostic;
            if (!TrySplitTagContent(tagContent, out propertyNameAndDestructuring, out format, out alignment, out tagContentDiagnostic))
            {
                tagContentDiagnostic.StartIndex += first + 1;
                return tagContentDiagnostic;
            }

            var propertyName = propertyNameAndDestructuring;
            bool hasDestructuring = IsValidInDestructuringHint(propertyName[0]);
            if (hasDestructuring)
                propertyName = propertyName.Substring(1);

            if (propertyName == "")
                return new MessageTemplateDiagnostic(first, rawText.Length, "Found property with destructuring hint but without name");

            for (var i = 0; i < propertyName.Length; ++i)
            {
                var c = propertyName[i];
                if (!IsValidInPropertyName(c))
                    return new MessageTemplateDiagnostic(first + (hasDestructuring ? 1 : 0) + 1 + i, 1, "Found invalid character '" + c.ToString() + "' in property name");
            }

            if (format != null)
            {
                for (var i = 0; i < format.Length; ++i)
                {
                    var c = format[i];
                    if (!IsValidInFormat(c))
                        return new MessageTemplateDiagnostic(first + propertyNameAndDestructuring.Length + (alignment?.Length + 1 ?? 0) + 2 + i, 1, "Found invalid character '" + c.ToString() + "' in property format");
                }
            }

            if (alignment != null)
            {
                for (var i = 0; i < alignment.Length; ++i)
                {
                    var c = alignment[i];
                    if (!IsValidInAlignment(c))
                        return new MessageTemplateDiagnostic(first + propertyNameAndDestructuring.Length + 2 + i, 1, "Found invalid character '" + c.ToString() + "' in property alignment");
                }

                var lastDash = alignment.LastIndexOf('-');
                if (lastDash > 0)
                    return new MessageTemplateDiagnostic(first + propertyNameAndDestructuring.Length + 2 + lastDash, 1, "'-' character must be the first in alignment");

                var width = lastDash == -1 ?
                    int.Parse(alignment) :
                    int.Parse(alignment.Substring(1));

                if (width == 0)
                    return new MessageTemplateDiagnostic(first + propertyNameAndDestructuring.Length + 2, alignment.Length, "Found zero size alignment");
            }

            return null;
        }

        static bool TrySplitTagContent(string tagContent, out string propertyNameAndDestructuring, out string format, out string alignment, out MessageTemplateDiagnostic diagnostic)
        {
            var formatDelim = tagContent.IndexOf(':');
            var alignmentDelim = tagContent.IndexOf(',');
            if (formatDelim == -1 && alignmentDelim == -1)
            {
                propertyNameAndDestructuring = tagContent;
                format = null;
                alignment = null;
                diagnostic = null;
            }
            else
            {
                if (alignmentDelim == -1 || (formatDelim != -1 && alignmentDelim > formatDelim))
                {
                    propertyNameAndDestructuring = tagContent.Substring(0, formatDelim);
                    format = formatDelim == tagContent.Length - 1 ?
                        null :
                        tagContent.Substring(formatDelim + 1);
                    alignment = null;
                    diagnostic = null;
                }
                else
                {
                    propertyNameAndDestructuring = tagContent.Substring(0, alignmentDelim);
                    if (formatDelim == -1)
                    {
                        if (alignmentDelim == tagContent.Length - 1)
                        {
                            alignment = format = null;
                            diagnostic = new MessageTemplateDiagnostic(alignmentDelim, 1, "Found alignment specifier without alignment");
                            return false;
                        }

                        format = null;
                        alignment = tagContent.Substring(alignmentDelim + 1);
                    }
                    else
                    {
                        if (alignmentDelim == formatDelim - 1)
                        {
                            alignment = format = null;
                            diagnostic = new MessageTemplateDiagnostic(alignmentDelim, 1, "Found alignment specifier without alignment");
                            return false;
                        }

                        alignment = tagContent.Substring(alignmentDelim + 1, formatDelim - alignmentDelim - 1);
                        format = formatDelim == tagContent.Length - 1 ?
                            null :
                            tagContent.Substring(formatDelim + 1);
                    }
                }
            }

            diagnostic = null;
            return true;
        }

        static bool IsValidInPropertyTag(char c)
        {
            return IsValidInDestructuringHint(c) ||
                IsValidInPropertyName(c) ||
                IsValidInFormat(c) ||
                c == ':';
        }

        static bool IsValidInPropertyName(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        static bool IsValidInDestructuringHint(char c)
        {
            return c == '@' ||
                   c == '$';
        }

        static bool IsValidInAlignment(char c)
        {
            return char.IsDigit(c) ||
                   c == '-';
        }

        static bool IsValidInFormat(char c)
        {
            return c != '}' &&
                (char.IsLetterOrDigit(c) ||
                 char.IsPunctuation(c) ||
                 c == ' ');
        }

        static void ParseTextToken(int startAt, string messageTemplate, out int next)
        {
            var first = startAt;

            do
            {
                var nc = messageTemplate[startAt];
                if (nc == '{')
                {
                    if (startAt + 1 < messageTemplate.Length &&
                        messageTemplate[startAt + 1] == '{')
                    {
                        startAt++;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    if (nc == '}')
                    {
                        if (startAt + 1 < messageTemplate.Length &&
                            messageTemplate[startAt + 1] == '}')
                        {
                            startAt++;
                        }
                    }
                }

                startAt++;
            } while (startAt < messageTemplate.Length);

            next = startAt;
        }
    }

    class MessageTemplateDiagnostic
    {
        public int StartIndex { get; set; }
        public int Length { get; set; }
        public string Diagnostic { get; set; }

        public MessageTemplateDiagnostic(int startIndex, int length, string diagnostic = null)
        {
            StartIndex = startIndex;
            Length = length;
            Diagnostic = diagnostic;
        }
    }
}
