﻿using Cocona.Application;
using Cocona.Command;
using Cocona.Help.DocumentModel;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Cocona.Help
{
    public class CoconaCommandHelpProvider : ICoconaCommandHelpProvider
    {
        private readonly ICoconaApplicationMetadataProvider _applicationMetadataProvider;
        private readonly IServiceProvider _serviceProvider;

        public CoconaCommandHelpProvider(ICoconaApplicationMetadataProvider applicationMetadataProvider, IServiceProvider serviceProvider)
        {
            _applicationMetadataProvider = applicationMetadataProvider;
            _serviceProvider = serviceProvider;
        }

        public string CreateUsageCommandOptionsAndArgs(CommandDescriptor command)
        {
            var sb = new StringBuilder();
            sb.Append(_applicationMetadataProvider.GetExecutableName());

            if (!command.IsPrimaryCommand)
            {
                sb.Append(" ");
                sb.Append(command.Name);
            }

            if (command.Options.Any())
            {
                foreach (var opt in command.Options)
                {
                    sb.Append(" ");
                    if (opt.OptionType == typeof(bool))
                    {
                        sb.Append($"[--{opt.Name}]");
                    }
                    else
                    {
                        sb.Append($"[--{opt.Name} <{opt.ValueName}>]");
                    }
                }
            }

            if (command.Arguments.Any())
            {
                foreach (var arg in command.Arguments)
                {
                    sb.Append(" ");
                    if (arg.IsEnumerableLike)
                    {
                        sb.Append($"{arg.Name}0 ... {arg.Name}N");
                    }
                    else
                    {
                        sb.Append(arg.Name);
                    }
                }
            }

            return sb.ToString();
        }

        public HelpMessage CreateCommandHelp(CommandDescriptor command)
        {
            var help = new HelpMessage();

            // Usage
            help.Children.Add(new HelpSection(new HelpHeading($"Usage: {CreateUsageCommandOptionsAndArgs(command)}")));

            // Description
            if (!string.IsNullOrWhiteSpace(command.Description))
            {
                help.Children.Add(new HelpSection(new HelpParagraph(command.Description)));
            }

            // Arguments
            if (command.Arguments.Any())
            {
                help.Children.Add(new HelpSection(
                    new HelpHeading("Arguments:"),
                    new HelpSection(
                        new HelpLabelDescriptionList(
                            command.Arguments
                                .Select((x, i) =>
                                    new HelpLabelDescriptionListItem(
                                        $"{i}: {x.Name}",
                                        $"{x.Description}{(x.IsRequired ? " (Required)" : (" (DefaultValue=" + x.DefaultValue.Value + ")"))}"
                                    )
                                )
                                .ToArray()
                        )
                    )
                ));
            }

            // Options
            if (command.Options.Any())
            {
                help.Children.Add(new HelpSection(
                    new HelpHeading("Options:"),
                    new HelpSection(
                        new HelpLabelDescriptionList(
                            command.Options
                                .Select((x, i) =>
                                    new HelpLabelDescriptionListItem(
                                        (x.ShortName.Any() ? string.Join(", ", x.ShortName.Select(x => $"-{x}")) + ", " : "") + $"--{x.Name}" + (x.OptionType != typeof(bool) ? $" <{x.ValueName}>" : ""),
                                        $"{x.Description}{(x.IsRequired ? " (Required)" : (" (DefaultValue: " + x.DefaultValue.Value + ")"))}"
                                    )
                                )
                                .ToArray()
                        )
                    )
                ));
            }

            // Transform help document
            var transformAttrs = command.Method.GetCustomAttributes<TransformHelpAttribute>();
            if (transformAttrs.Any())
            {
                foreach (var transformAttr in transformAttrs)
                {
                    var transformer = (ICoconaHelpTransformer)ActivatorUtilities.GetServiceOrCreateInstance(_serviceProvider, transformAttr.Transformer);
                    transformer.TransformHelp(help, command);
                }
            }

            return help;
        }

        public HelpMessage CreateCommandsIndexHelp(CommandCollection commandCollection)
        {
            var help = new HelpMessage();

            // Usage
            var usageSection = new HelpSection();
            if (commandCollection.All.Count != 1)
            {
                usageSection.Children.Add(new HelpHeading($"Usage: {_applicationMetadataProvider.GetExecutableName()} [command]"));
            }
            if (commandCollection.Primary != null)
            {
                usageSection.Children.Add(new HelpHeading($"Usage: {CreateUsageCommandOptionsAndArgs(commandCollection.Primary)}"));
            }
            help.Children.Add(usageSection);

            // Description
            var description = string.IsNullOrWhiteSpace(commandCollection.Description)
                ? _applicationMetadataProvider.GetDescription()
                : commandCollection.Description;

            if (!string.IsNullOrWhiteSpace(description))
            {
                help.Children.Add(new HelpSection(new HelpParagraph(description)));
            }

            // Commands
            var commandsExceptPrimary = commandCollection.All.Where(x => !x.IsPrimaryCommand).ToArray();
            if (commandsExceptPrimary.Any())
            {
                help.Children.Add(new HelpSection(
                    new HelpHeading("Commands:"),
                    new HelpSection(
                        new HelpLabelDescriptionList(
                            commandsExceptPrimary
                                .Select((x, i) =>
                                    new HelpLabelDescriptionListItem(x.Name, x.Description)
                                )
                                .ToArray()
                        )
                    )
                ));
            }

            // Options
            if (commandCollection.Primary != null && commandCollection.Primary.Options.Any())
            {
                help.Children.Add(new HelpSection(
                    new HelpHeading("Options:"),
                    new HelpSection(
                        new HelpLabelDescriptionList(
                            commandCollection.Primary.Options
                                .Select((x, i) =>
                                    new HelpLabelDescriptionListItem(
                                        (x.ShortName.Any() ? string.Join(", ", x.ShortName.Select(x => $"-{x}")) + ", " : "") + $"--{x.Name}" + (x.OptionType != typeof(bool) ? $" <{x.ValueName}>" : ""),
                                        $"{x.Description}{(x.IsRequired ? " (Required)" : (" (DefaultValue: " + x.DefaultValue.Value + ")"))}"
                                    )
                                )
                                .ToArray()
                        )
                    )
                ));
            }

            // Transform help document
            if (commandCollection.Primary != null)
            {
                var transformAttrs = commandCollection.Primary.Method.GetCustomAttributes<TransformHelpAttribute>();
                if (transformAttrs.Any())
                {
                    foreach (var transformAttr in transformAttrs)
                    {
                        var transformer = (ICoconaHelpTransformer)ActivatorUtilities.GetServiceOrCreateInstance(_serviceProvider, transformAttr.Transformer);
                        transformer.TransformHelp(help, commandCollection.Primary);
                    }
                }
            }

            return help;
        }

        public HelpMessage CreateVersionHelp()
        {
            var prodName = _applicationMetadataProvider.GetProductName();
            var version = _applicationMetadataProvider.GetVersion();

            return new HelpMessage(new HelpSection(new HelpHeading($"{prodName} {version}")));
        }
    }
}
