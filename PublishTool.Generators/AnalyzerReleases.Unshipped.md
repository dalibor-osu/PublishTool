; Unshipped analyzer
release ; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

 Rule ID | Category        | Severity | Notes                                                   
---------|-----------------|----------|---------------------------------------------------------
 PT0001  | PublishTool.Cli | Error    | CommandParserGenerator, duplicate option alias          
 PT0002  | PublishTool.Cli | Error    | CommandParserGenerator, unsupported option type         
 PT0003  | PublishTool.Cli | Error    | CommandParserGenerator, option without an alias         
 PT0004  | PublishTool.Cli | Error    | CommandParserGenerator, command without an options type 
 PT0005  | PublishTool.Cli | Error    | CommandParserGenerator, option is not settable          
 PT0006  | PublishTool.Cli | Error    | CommandParserGenerator, parser template failed          
 PT0007  | PublishTool.Cli | Error    | CommandParserGenerator, invalid option parser method    
