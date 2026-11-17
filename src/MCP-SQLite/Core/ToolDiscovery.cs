using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MCP_SQLite.Core
{
    /// <summary>
    /// Discovers all ITool implementations in the assembly and builds MCP definitions.
    /// Tools are constructed via a factory.
    /// </summary>
    public static class ToolDiscovery
    {
        public static Dictionary<string, ITool> DiscoverTools(Func<Type, ITool> factory)
        {
            var tools = new Dictionary<string, ITool>();
            var assembly = Assembly.GetExecutingAssembly();

            var toolTypes = assembly.GetTypes()
                .Where(t => typeof(ITool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in toolTypes)
            {
                try
                {
                    var tool = factory(type);
                    if (tool != null && !string.IsNullOrEmpty(tool.Name))
                        tools[tool.Name] = tool;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to instantiate tool {type.Name}: {ex.Message}");
                }
            }

            return tools;
        }

        public static List<McpToolDefinition> GenerateToolDefinitions(Dictionary<string, ITool> tools)
        {
            var definitions = new List<McpToolDefinition>();

            foreach (var tool in tools.Values)
            {
                var definition = new McpToolDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = new McpInputSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, McpPropertySchema>(),
                        Required = new List<string>()
                    }
                };

                foreach (var param in tool.Params.Parameters)
                {
                    var propertySchema = new McpPropertySchema
                    {
                        Type = param.Type,
                        Description = param.Description
                    };

                    if (param.EnumValues != null && param.EnumValues.Length > 0)
                        propertySchema.Enum = param.EnumValues;

                    if (param.Type == "array")
                        propertySchema.Items = new McpPropertySchema { Type = string.IsNullOrEmpty(param.ItemsType) ? "object" : param.ItemsType };

                    if (!string.IsNullOrEmpty(param.DefaultValue))
                    {
                        switch (param.Type)
                        {
                            case "integer":
                                if (int.TryParse(param.DefaultValue, out int intVal))
                                    propertySchema.Default = intVal;
                                break;
                            case "number":
                                if (double.TryParse(param.DefaultValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dVal))
                                    propertySchema.Default = dVal;
                                break;
                            case "boolean":
                                if (bool.TryParse(param.DefaultValue, out bool boolVal))
                                    propertySchema.Default = boolVal;
                                break;
                            default:
                                propertySchema.Default = param.DefaultValue;
                                break;
                        }
                    }

                    definition.InputSchema.Properties[param.Name] = propertySchema;

                    if (param.Required)
                        definition.InputSchema.Required.Add(param.Name);
                }

                if (definition.InputSchema.Required.Count == 0)
                    definition.InputSchema.Required = null;

                definitions.Add(definition);
            }

            return definitions;
        }
    }
}
