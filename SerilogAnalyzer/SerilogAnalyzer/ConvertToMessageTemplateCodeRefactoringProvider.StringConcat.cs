﻿// Copyright 2016 Robin Sue
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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SerilogAnalyzer
{
    public partial class ConvertToMessageTemplateCodeFixProvider
    {
        private async Task<Document> ConvertStringConcatToMessageTemplateAsync(Document document, ExpressionSyntax stringConcat, InvocationExpressionSyntax logger, CancellationToken cancellationToken)
        {
            GetFormatStringAndExpressionsFromStringConcat(stringConcat, out var format, out var expressions);

            return await InlineFormatAndArgumentsIntoLoggerStatementAsync(document, stringConcat, logger, format, expressions, cancellationToken);
        }

        private static void GetFormatStringAndExpressionsFromStringConcat(ExpressionSyntax stringConcat, out InterpolatedStringExpressionSyntax format, out List<ExpressionSyntax> expressions)
        {
            var concatExpressions = new List<ExpressionSyntax>();
            void FindExpressions(ExpressionSyntax exp)
            {
                switch (exp)
                {
                    case BinaryExpressionSyntax binary when binary.OperatorToken.IsKind(SyntaxKind.PlusToken):
                        FindExpressions(binary.Left);
                        FindExpressions(binary.Right);
                        break;
                    case ParenthesizedExpressionSyntax parens:
                        FindExpressions(parens.Expression);
                        break;
                    case LiteralExpressionSyntax literal:
                        concatExpressions.Add(literal);
                        break;
                    default:
                        concatExpressions.Add(exp.Parent is ParenthesizedExpressionSyntax paren ? paren : exp);
                        break;
                }
            }
            FindExpressions(stringConcat);

            var sb = new StringBuilder();
            var replacements = new List<string>();
            bool shouldUseVerbatim = false;
            int argumentPosition = 0;
            foreach (var child in concatExpressions)
            {
                switch (child)
                {
                    case LiteralExpressionSyntax literal:
                        sb.Append(literal.Token.ValueText);
                        shouldUseVerbatim |= literal.Token.Text.StartsWith("@", System.StringComparison.Ordinal) && ContainsQuotesOrLineBreaks(literal.Token.ValueText);
                        break;
                    case ExpressionSyntax exp:

                        sb.Append("{");
                        sb.Append(ConversionName);
                        sb.Append(argumentPosition++);
                        sb.Append("}");

                        break;
                }
            }

            if (shouldUseVerbatim)
            {
                for (int i = 0; i < sb.Length; i++)
                {
                    if (IsForbiddenInVerbatimString(sb[i]))
                    {
                        shouldUseVerbatim = false;
                        break;
                    }
                }
            }

            var text = ObjectDisplay.FormatLiteral(sb.ToString(), useQuotes: true, escapeNonPrintable: !shouldUseVerbatim);

            format = (InterpolatedStringExpressionSyntax)SyntaxFactory.ParseExpression("$" + text);
            expressions = concatExpressions.Where(x => !(x is LiteralExpressionSyntax)).ToList();
        }

        private static bool IsForbiddenInVerbatimString(char c)
        {
            switch (c)
            {
                case '\a':
                case '\b':
                case '\f':
                case '\v':
                case '\0':
                    return true;
            }
            
            return false;
        }

        private static bool ContainsQuotesOrLineBreaks(string s)
        {
            foreach (char c in s)
            {
                if (c == '\r' || c == '\n' || c == '"')
                {
                    return true;
                }
            }

            return false;
        }
    }
}