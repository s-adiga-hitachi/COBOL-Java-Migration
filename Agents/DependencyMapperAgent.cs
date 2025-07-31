using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using CobolToQuarkusMigration.Agents.Interfaces;
using CobolToQuarkusMigration.Models;
using CobolToQuarkusMigration.Helpers;
using System.Text.RegularExpressions;
using System.Text;
using System.Diagnostics;

namespace CobolToQuarkusMigration.Agents;

/// <summary>
/// Implementation of the COBOL dependency mapper agent with enhanced API call tracking.
/// </summary>
public class DependencyMapperAgent : IDependencyMapperAgent
{
    private readonly IKernelBuilder _kernelBuilder;
    private readonly ILogger<DependencyMapperAgent> _logger;
    private readonly string _modelId;
    private readonly EnhancedLogger? _enhancedLogger;
    private readonly ChatLogger? _chatLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyMapperAgent"/> class.
    /// </summary>
    /// <param name="kernelBuilder">The kernel builder.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="modelId">The model ID to use for analysis.</param>
    /// <param name="enhancedLogger">Enhanced logger for API call tracking.</param>
    /// <param name="chatLogger">Chat logger for Azure OpenAI conversation tracking.</param>
    public DependencyMapperAgent(IKernelBuilder kernelBuilder, ILogger<DependencyMapperAgent> logger, string modelId, EnhancedLogger? enhancedLogger = null, ChatLogger? chatLogger = null)
    {
        _kernelBuilder = kernelBuilder;
        _logger = logger;
        _modelId = modelId;
        _enhancedLogger = enhancedLogger;
        _chatLogger = chatLogger;
    }

    /// <inheritdoc/>
    public async Task<DependencyMap> AnalyzeDependenciesAsync(List<CobolFile> cobolFiles, List<CobolAnalysis> analyses)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation("Analyzing dependencies for {Count} COBOL files", cobolFiles.Count);
        _enhancedLogger?.LogBehindTheScenes("AI_PROCESSING", "DEPENDENCY_ANALYSIS_START", 
            $"Starting dependency analysis for {cobolFiles.Count} COBOL files");

        var dependencyMap = new DependencyMap();
        var kernel = _kernelBuilder.Build();

        try
        {
            // First, analyze copybook usage patterns
            _enhancedLogger?.LogBehindTheScenes("PROCESSING", "COPYBOOK_ANALYSIS", 
                "Analyzing copybook usage patterns");
            dependencyMap.CopybookUsage = await AnalyzeCopybookUsageAsync(cobolFiles);
            
            // Build reverse dependencies
            _enhancedLogger?.LogBehindTheScenes("PROCESSING", "REVERSE_DEPENDENCIES", 
                "Building reverse dependency relationships");
            BuildReverseDependencies(dependencyMap);
            
            // Analyze detailed dependencies using AI
            _enhancedLogger?.LogBehindTheScenes("AI_PROCESSING", "DETAILED_ANALYSIS", 
                "Performing AI-powered detailed dependency analysis");
            await AnalyzeDetailedDependenciesAsync(kernel, cobolFiles, analyses, dependencyMap);
            
            // Calculate metrics
            _enhancedLogger?.LogBehindTheScenes("PROCESSING", "METRICS_CALCULATION", 
                "Calculating dependency metrics and statistics");
            CalculateMetrics(dependencyMap, cobolFiles);
            
            // Generate Mermaid diagram
            _enhancedLogger?.LogBehindTheScenes("AI_PROCESSING", "DIAGRAM_GENERATION", 
                "Generating Mermaid dependency diagram");
            dependencyMap.MermaidDiagram = await GenerateMermaidDiagramAsync(dependencyMap);

            stopwatch.Stop();
            _enhancedLogger?.LogBehindTheScenes("AI_PROCESSING", "DEPENDENCY_ANALYSIS_COMPLETE", 
                $"Completed dependency analysis in {stopwatch.ElapsedMilliseconds}ms. Found {dependencyMap.Dependencies.Count} dependencies", dependencyMap);

            _logger.LogInformation("Dependency analysis completed. Found {Count} dependencies", dependencyMap.Dependencies.Count);
            
            return dependencyMap;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _enhancedLogger?.LogBehindTheScenes("ERROR", "DEPENDENCY_ANALYSIS_ERROR", 
                $"Failed dependency analysis after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}", ex);
            
            _logger.LogError(ex, "Error analyzing COBOL dependencies");
            throw;
        }
    }

    /// <inheritdoc/>
    public Task<Dictionary<string, List<string>>> AnalyzeCopybookUsageAsync(List<CobolFile> cobolFiles)
    {
        _logger.LogInformation("Analyzing copybook usage patterns");
        
        var copybookUsage = new Dictionary<string, List<string>>();
        
        foreach (var cobolFile in cobolFiles.Where(f => f.FileName.EndsWith(".cbl")))
        {
            var copybooks = ExtractCopybookReferences(cobolFile.Content);
            copybookUsage[cobolFile.FileName] = copybooks;
            
            _logger.LogDebug("Program {Program} uses {Count} copybooks: {Copybooks}", 
                cobolFile.FileName, copybooks.Count, string.Join(", ", copybooks));
        }
        
        return Task.FromResult(copybookUsage);
    }

    /// <inheritdoc/>
    public async Task<string> GenerateMermaidDiagramAsync(DependencyMap dependencyMap)
    {
        _logger.LogInformation("Generating Mermaid diagram for dependency map");
        
        var kernel = _kernelBuilder.Build();
        
        try
        {
            var systemPrompt = @"
You are an expert in creating Mermaid diagrams for software architecture visualization. 
Your task is to create a clear, well-organized Mermaid flowchart that shows COBOL program dependencies.

Guidelines:
1. Use 'graph TB' (top-bottom) or 'graph LR' (left-right) layout based on complexity
2. Group related items using subgraphs
3. Use different colors/styles for programs (.cbl) vs copybooks (.cpy)
4. Show clear dependency arrows
5. Keep the diagram readable and not overcrowded
6. Use meaningful node IDs and labels
7. Add styling for better visual appeal

Return only the Mermaid diagram code, no additional text.
";

            var prompt = $@"
Create a Mermaid diagram for the following COBOL dependency structure:

Programs and their copybook dependencies:
{string.Join("\n", dependencyMap.CopybookUsage.Select(kv => $"- {kv.Key}: {string.Join(", ", kv.Value)}"))}

Dependency relationships:
{string.Join("\n", dependencyMap.Dependencies.Select(d => $"- {d.SourceFile} → {d.TargetFile} ({d.DependencyType})"))}

Total: {dependencyMap.Metrics.TotalPrograms} programs, {dependencyMap.Metrics.TotalCopybooks} copybooks

Create a clear, organized Mermaid diagram that shows these relationships.
";

            var executionSettings = new OpenAIPromptExecutionSettings
            {
                MaxTokens = 32768,
                Temperature = 0.1,
                TopP = 0.5
            };

            var fullPrompt = $"{systemPrompt}\n\n{prompt}";
            var kernelArguments = new KernelArguments(executionSettings);
            
            // Log user message to chat logger
            _chatLogger?.LogUserMessage("DependencyMapperAgent", "dependency-diagram", prompt, systemPrompt);
            
            var functionResult = await kernel.InvokePromptAsync(fullPrompt, kernelArguments);
            var mermaidDiagram = functionResult.GetValue<string>() ?? string.Empty;
            
            // Log AI response to chat logger
            _chatLogger?.LogAIResponse("DependencyMapperAgent", "dependency-diagram", mermaidDiagram);
            
            // Clean up the diagram (remove markdown code blocks if present)
            mermaidDiagram = mermaidDiagram.Replace("```mermaid", "").Replace("```", "").Trim();
            
            _logger.LogInformation("Mermaid diagram generated successfully");
            
            return mermaidDiagram;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Mermaid diagram");
            return GenerateFallbackMermaidDiagram(dependencyMap);
        }
    }

    private List<string> ExtractCopybookReferences(string cobolContent)
    {
        var copybooks = new List<string>();
        
        // Regex patterns to match COPY statements
        var patterns = new[]
        {
            @"COPY\s+([A-Za-z0-9_-]+)",           // COPY COPYBOOK
            @"COPY\s+([A-Za-z0-9_-]+)\.cpy",      // COPY COPYBOOK.cpy
            @"INCLUDE\s+([A-Za-z0-9_-]+)",        // INCLUDE statement
            @"COPY\s+'([A-Za-z0-9_-]+)'",         // COPY 'COPYBOOK'
        };
        
        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(cobolContent, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var copybookName = match.Groups[1].Value;
                
                // Ensure it has .cpy extension
                if (!copybookName.EndsWith(".cpy"))
                {
                    copybookName += ".cpy";
                }
                
                if (!copybooks.Contains(copybookName))
                {
                    copybooks.Add(copybookName);
                }
            }
        }
        
        return copybooks;
    }

    private void BuildReverseDependencies(DependencyMap dependencyMap)
    {
        foreach (var kvp in dependencyMap.CopybookUsage)
        {
            var program = kvp.Key;
            var copybooks = kvp.Value;
            
            foreach (var copybook in copybooks)
            {
                if (!dependencyMap.ReverseDependencies.ContainsKey(copybook))
                {
                    dependencyMap.ReverseDependencies[copybook] = new List<string>();
                }
                
                if (!dependencyMap.ReverseDependencies[copybook].Contains(program))
                {
                    dependencyMap.ReverseDependencies[copybook].Add(program);
                }
            }
        }
    }

    private async Task AnalyzeDetailedDependenciesAsync(Kernel kernel, List<CobolFile> cobolFiles, 
        List<CobolAnalysis> analyses, DependencyMap dependencyMap)
    {
        _logger.LogInformation("Performing detailed dependency analysis using AI");
        int apiCallId = 0;
        
        try
        {
            // Create dependency relationships for each copybook usage
            foreach (var kvp in dependencyMap.CopybookUsage)
            {
                var program = kvp.Key;
                var copybooks = kvp.Value;
                
                foreach (var copybook in copybooks)
                {
                    var dependency = new DependencyRelationship
                    {
                        SourceFile = program,
                        TargetFile = copybook,
                        DependencyType = "COPY",
                        Context = "Copybook inclusion"
                    };
                    
                    dependencyMap.Dependencies.Add(dependency);
                }
            }

            // Perform AI-powered analysis for additional insights
            if (cobolFiles.Any())
            {
                var systemPrompt = @"
You are an expert COBOL dependency analyzer. Analyze the provided COBOL code structure and identify:
1. Data flow dependencies between copybooks
2. Potential circular dependencies
3. Modularity recommendations
4. Legacy patterns that affect dependencies

Provide a brief analysis of the dependency structure and any recommendations.
";

                var fileStructure = string.Join("\n", cobolFiles.Take(5).Select(f => 
                    $"File: {f.FileName}\nType: {(f.FileName.EndsWith(".cbl") ? "Program" : "Copybook")}\nSize: {f.Content.Length} chars"));

                var prompt = $@"
Analyze the dependency structure of this COBOL project:

{fileStructure}

Copybook usage patterns:
{string.Join("\n", dependencyMap.CopybookUsage.Take(10).Select(kvp => 
    $"{kvp.Key} uses: {string.Join(", ", kvp.Value)}"))}

Provide insights about the dependency architecture.
";

                // Log API call start
                apiCallId = _enhancedLogger?.LogApiCallStart(
                    "DependencyMapperAgent", 
                    "ChatCompletion", 
                    "OpenAI/AnalyzeDependencies",
                    _modelId,
                    $"Analyzing dependencies for {cobolFiles.Count} files"
                ) ?? 0;

                _enhancedLogger?.LogBehindTheScenes("API_CALL", "DEPENDENCY_INSIGHTS_REQUEST", 
                    $"Requesting AI analysis of dependency structure for {cobolFiles.Count} files");

                var executionSettings = new OpenAIPromptExecutionSettings
                {
                    MaxTokens = 32768,
                    Temperature = 0.2
                };

                var kernelArguments = new KernelArguments(executionSettings);
                var fullPrompt = $"{systemPrompt}\n\n{prompt}";

                // Log user message to chat logger
                _chatLogger?.LogUserMessage("DependencyMapperAgent", "dependency-analysis", prompt, systemPrompt);

                var functionResult = await kernel.InvokePromptAsync(fullPrompt, kernelArguments);
                var insights = functionResult.GetValue<string>() ?? string.Empty;

                // Log AI response to chat logger
                _chatLogger?.LogAIResponse("DependencyMapperAgent", "dependency-analysis", insights);

                // Log API call completion
                _enhancedLogger?.LogApiCallEnd(apiCallId, insights, insights.Length / 4, 0.001m);
                _enhancedLogger?.LogBehindTheScenes("API_CALL", "DEPENDENCY_INSIGHTS_RESPONSE", 
                    $"Received dependency insights ({insights.Length} chars)");

                // Store insights in the dependency map
                dependencyMap.AnalysisInsights = insights;
            }
        }
        catch (Exception ex)
        {
            if (apiCallId > 0)
            {
                _enhancedLogger?.LogApiCallError(apiCallId, ex.Message);
            }
            
            _enhancedLogger?.LogBehindTheScenes("ERROR", "DEPENDENCY_ANALYSIS_ERROR", 
                $"Error in detailed dependency analysis: {ex.Message}", ex);
            
            _logger.LogWarning(ex, "Error during detailed dependency analysis, continuing with basic analysis");
        }
    }

    private void CalculateMetrics(DependencyMap dependencyMap, List<CobolFile> cobolFiles)
    {
        var programs = cobolFiles.Where(f => f.FileName.EndsWith(".cbl")).ToList();
        var copybooks = cobolFiles.Where(f => f.FileName.EndsWith(".cpy")).ToList();
        
        dependencyMap.Metrics.TotalPrograms = programs.Count;
        dependencyMap.Metrics.TotalCopybooks = copybooks.Count;
        dependencyMap.Metrics.TotalDependencies = dependencyMap.Dependencies.Count;
        
        if (programs.Count > 0)
        {
            dependencyMap.Metrics.AverageDependenciesPerProgram = 
                (double)dependencyMap.Dependencies.Count / programs.Count;
        }
        
        // Find most used copybook
        if (dependencyMap.ReverseDependencies.Any())
        {
            var mostUsed = dependencyMap.ReverseDependencies
                .OrderByDescending(kvp => kvp.Value.Count)
                .First();
            
            dependencyMap.Metrics.MostUsedCopybook = mostUsed.Key;
            dependencyMap.Metrics.MostUsedCopybookCount = mostUsed.Value.Count;
        }
        
        _logger.LogInformation("Calculated metrics: {Programs} programs, {Copybooks} copybooks, {Dependencies} dependencies", 
            dependencyMap.Metrics.TotalPrograms, 
            dependencyMap.Metrics.TotalCopybooks, 
            dependencyMap.Metrics.TotalDependencies);
    }

    private string GenerateFallbackMermaidDiagram(DependencyMap dependencyMap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph TB");
        sb.AppendLine("    subgraph \"COBOL Programs\"");
        
        var programs = dependencyMap.CopybookUsage.Keys.ToList();
        for (int i = 0; i < programs.Count; i++)
        {
            sb.AppendLine($"        P{i}[\"{programs[i]}\"]");
        }
        
        sb.AppendLine("    end");
        sb.AppendLine("    subgraph \"Copybooks\"");
        
        var copybooks = dependencyMap.ReverseDependencies.Keys.ToList();
        for (int i = 0; i < copybooks.Count; i++)
        {
            sb.AppendLine($"        C{i}[\"{copybooks[i]}\"]");
        }
        
        sb.AppendLine("    end");
        
        // Add dependencies
        foreach (var kvp in dependencyMap.CopybookUsage)
        {
            var programIndex = programs.IndexOf(kvp.Key);
            foreach (var copybook in kvp.Value)
            {
                var copybookIndex = copybooks.IndexOf(copybook);
                if (copybookIndex >= 0)
                {
                    sb.AppendLine($"    P{programIndex} --> C{copybookIndex}");
                }
            }
        }
        
        // Add styling
        sb.AppendLine("    classDef programClass fill:#81c784");
        sb.AppendLine("    classDef copybookClass fill:#ffb74d");
        
        for (int i = 0; i < programs.Count; i++)
        {
            sb.AppendLine($"    class P{i} programClass");
        }
        
        for (int i = 0; i < copybooks.Count; i++)
        {
            sb.AppendLine($"    class C{i} copybookClass");
        }
        
        return sb.ToString();
    }
}
