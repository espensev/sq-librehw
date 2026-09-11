[CmdletBinding()]
param(
    [switch] $LauncherConvergenceOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$opsRoot = $PSScriptRoot
$commonScript = Join-Path $opsRoot 'LhmLocalRelease.Common.ps1'
$installScript = Join-Path $opsRoot 'Install-LibreHardwareMonitorRelease.ps1'
$rollbackScript = Join-Path $opsRoot 'Restore-LibreHardwareMonitorRelease.ps1'
$relocationScript = Join-Path $opsRoot 'Relocate-LibreHardwareMonitorDataRoot.ps1'
$runtimeMigrationScript = Join-Path $opsRoot 'Move-LibreHardwareMonitorRuntimeRoot.ps1'
$finalizeScript = Join-Path $opsRoot 'Finalize-LibreHardwareMonitorCutover.ps1'
$legacyRecoveryScript = Join-Path $opsRoot 'Restore-LegacyLibreHardwareMonitorStartup.ps1'
$preStableRecoveryScript =
    Join-Path $opsRoot 'Restore-PreStableLibreHardwareMonitorStartup.ps1'
$canonicalLauncher = Join-Path $opsRoot 'Start-LibreHardwareMonitor.ps1'
$canonicalPublicShim = Join-Path $opsRoot 'librehw.cmd'
$launcherConvergenceScript =
    Join-Path $opsRoot 'Sync-LibreHardwareMonitorLauncher.ps1'
$cleanupScript = Join-Path $repositoryRoot 'eng\Clear-LhmRepositoryBuildOutputs.ps1'

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )
    if (-not $Condition) {
        throw "ASSERTION FAILED: $Message"
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [Parameter(Mandatory)]
        [string] $MessagePattern
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notmatch $MessagePattern) {
            throw "Expected failure matching '$MessagePattern', got '$($_.Exception.Message)'."
        }
        return
    }
    throw "Expected failure matching '$MessagePattern', but the action succeeded."
}

function Assert-LhmNoLoadTimeDirectiveText {
    param(
        [Parameter(Mandatory)]
        [string] $Text,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $loadTimeDirectivePattern =
        '(?is)\busing(?:\s|`\r?\n|<\#.*?\#>)+(?:module|assembly)\b'
    Assert-True ($Text -notmatch $loadTimeDirectivePattern) `
        "$Label contains a load-time using module or assembly directive."
}

function Get-LhmScriptAst {
    param([Parameter(Mandatory)][string] $Path)

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $scriptText = [System.IO.File]::ReadAllText($resolvedPath)
    Assert-LhmNoLoadTimeDirectiveText -Text $scriptText -Label $Path
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $resolvedPath,
        [ref]$tokens,
        [ref]$errors)
    Assert-True ($errors.Count -eq 0) "PowerShell parser errors in '$Path'."
    return $ast
}

function Get-LhmNearestFunctionDefinitionAst {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.Ast] $Node
    )

    $current = $Node.Parent
    while ($null -ne $current) {
        if ($current -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
            return $current
        }
        $current = $current.Parent
    }
    return $null
}

function Get-LhmTopLevelFunctionMap {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst
    )

    $functions = @{}
    foreach ($statement in $ScriptAst.EndBlock.Statements) {
        if ($statement -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) {
            continue
        }
        Assert-True (-not $functions.ContainsKey($statement.Name)) `
            "Duplicate top-level function '$($statement.Name)'."
        $functions[$statement.Name] = $statement
    }
    return $functions
}

function Assert-LhmStandalonePipelineCommand {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.CommandAst] $CommandAst,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $pipeline = $CommandAst.Parent
    $isBackground = $false
    if ($pipeline -is [System.Management.Automation.Language.PipelineAst]) {
        $backgroundProperty = $pipeline.PSObject.Properties['Background']
        if ($null -ne $backgroundProperty) {
            $isBackground = [bool]$backgroundProperty.Value
        }
    }
    Assert-True (
        $pipeline -is [System.Management.Automation.Language.PipelineAst] -and
        $pipeline.PipelineElements.Count -eq 1 -and
        [object]::ReferenceEquals($pipeline.PipelineElements[0], $CommandAst) -and
        -not $isBackground
    ) "$Label must be a foreground command with no pipeline input or output."
}

function Assert-LhmBareIdentityCommand {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.CommandAst] $CommandAst,

        [Parameter(Mandatory)]
        [string] $ExpectedName,

        [Parameter(Mandatory)]
        [string] $Label
    )

    Assert-True (
        $CommandAst.GetCommandName() -ceq $ExpectedName -and
        $CommandAst.InvocationOperator -eq
            [System.Management.Automation.Language.TokenKind]::Unknown -and
        $CommandAst.CommandElements.Count -eq 1 -and
        @($CommandAst.Redirections).Count -eq 0
    ) "$Label must directly invoke bare command '$ExpectedName' without arguments."
    Assert-LhmStandalonePipelineCommand -CommandAst $CommandAst -Label $Label
}

function Assert-LhmCanonicalIdentityAssignment {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.AssignmentStatementAst] $AssignmentAst,

        [Parameter(Mandatory)]
        [System.Management.Automation.Language.CommandAst] $CommandAst,

        [Parameter(Mandatory)]
        [string] $Label
    )

    Assert-True (
        $AssignmentAst.Left -is
            [System.Management.Automation.Language.VariableExpressionAst] -and
        $AssignmentAst.Left.Extent.Text -ceq '$null' -and
        $AssignmentAst.Operator -eq
            [System.Management.Automation.Language.TokenKind]::Equals -and
        [object]::ReferenceEquals($AssignmentAst.Right, $CommandAst.Parent)
    ) "$Label must use the identity command as the complete RHS of a `$null assignment."
}

function Assert-LhmLiteralThrowStatement {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ThrowStatementAst] $ThrowAst,

        [Parameter(Mandatory)]
        [string] $Label,

        [AllowNull()]
        [object] $ExpectedMessage = $null
    )

    $pipeline = $ThrowAst.Pipeline
    $isBackground = $false
    if ($pipeline -is [System.Management.Automation.Language.PipelineAst]) {
        $backgroundProperty = $pipeline.PSObject.Properties['Background']
        if ($null -ne $backgroundProperty) {
            $isBackground = [bool]$backgroundProperty.Value
        }
    }
    Assert-True (
        -not $ThrowAst.IsRethrow -and
        $pipeline -is [System.Management.Automation.Language.PipelineAst] -and
        $pipeline.PipelineElements.Count -eq 1 -and
        -not $isBackground -and
        $pipeline.PipelineElements[0] -is
            [System.Management.Automation.Language.CommandExpressionAst] -and
        $pipeline.PipelineElements[0].Redirections.Count -eq 0 -and
        $pipeline.PipelineElements[0].Expression -is
            [System.Management.Automation.Language.StringConstantExpressionAst] -and
        ($null -eq $ExpectedMessage -or
            $pipeline.PipelineElements[0].Expression.Value -ceq $ExpectedMessage)
    ) "$Label must throw only its reviewed literal message."
}

function Assert-LhmPinnedTopLevelLiteral {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst,

        [Parameter(Mandatory)]
        [string] $VariableText,

        [Parameter(Mandatory)]
        [string] $ExpectedValue,

        [Parameter(Mandatory)]
        [string] $Label
    )

    Assert-True ($VariableText.StartsWith('$')) `
        "$Label pinned variable name must begin with a dollar sign."
    $semanticVariableName = $VariableText.Substring(1)
    $assignments = @($ScriptAst.EndBlock.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -ieq $semanticVariableName
    }, $true) | Where-Object {
        $null -eq (Get-LhmNearestFunctionDefinitionAst -Node $_)
    })
    Assert-True ($assignments.Count -eq 1) `
        "$Label must assign $VariableText exactly once at top level."
    $right = $assignments[0].Right
    Assert-True (
        $assignments[0].Operator -eq
            [System.Management.Automation.Language.TokenKind]::Equals -and
        $right -is [System.Management.Automation.Language.CommandExpressionAst] -and
        $right.Expression -is
            [System.Management.Automation.Language.StringConstantExpressionAst] -and
        $right.Expression.Value -ceq $ExpectedValue
    ) "$Label must keep $VariableText pinned to its reviewed literal."
}

function Assert-LhmKnownFolderVerifierAssignment {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst,

        [Parameter(Mandatory)]
        [string] $VariableText,

        [Parameter(Mandatory)]
        [string] $Label
    )

    Assert-True ($VariableText.StartsWith('$')) `
        "$Label verifier variable name must begin with a dollar sign."
    $semanticVariableName = $VariableText.Substring(1)
    $assignments = @($ScriptAst.EndBlock.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -ieq $semanticVariableName
    }, $true) | Where-Object {
        $null -eq (Get-LhmNearestFunctionDefinitionAst -Node $_)
    })
    Assert-True ($assignments.Count -eq 1) `
        "$Label must assign $VariableText exactly once at top level."
    $normalizedRight = $assignments[0].Right.Extent.Text -replace '\s+', ''
    $expectedRight =
        "[System.IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'),'common_dev\v2\Test-LocalMachineIdentity.ps1')"
    Assert-True (
        $assignments[0].Operator -eq
            [System.Management.Automation.Language.TokenKind]::Equals -and
        $normalizedRight -ceq $expectedRight
    ) "$Label must resolve $VariableText from the Windows LocalApplicationData known folder."
}

function Assert-LhmInertBypassSwitch {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst,

        [Parameter(Mandatory)]
        [string] $ParameterName,

        [Parameter(Mandatory)]
        [string] $Label,

        [switch] $RequireMandatory
    )

    $parameters = @($ScriptAst.ParamBlock.Parameters | Where-Object {
        $_.Name.VariablePath.UserPath -ieq $ParameterName
    })
    Assert-True ($parameters.Count -eq 1) `
        "$Label must declare exactly one $ParameterName bypass parameter."
    $parameter = $parameters[0]
    $attributes = @($parameter.Attributes)
    $typeConstraints = @($attributes | Where-Object {
        $_ -is [System.Management.Automation.Language.TypeConstraintAst]
    })
    $validationAttributes = @($attributes | Where-Object {
        $_ -isnot [System.Management.Automation.Language.TypeConstraintAst]
    })
    Assert-True (
        $typeConstraints.Count -eq 1 -and
        $typeConstraints[0].TypeName.FullName -ceq 'switch' -and
        $null -eq $parameter.DefaultValue
    ) "$Label $ParameterName bypass must be a default-false switch only."
    if ($RequireMandatory) {
        Assert-True (
            $validationAttributes.Count -eq 1 -and
            $validationAttributes[0] -is
                [System.Management.Automation.Language.AttributeAst] -and
            $validationAttributes[0].TypeName.FullName -ceq 'Parameter' -and
            $validationAttributes[0].PositionalArguments.Count -eq 0 -and
            $validationAttributes[0].NamedArguments.Count -eq 1 -and
            $validationAttributes[0].NamedArguments[0].ArgumentName -ceq 'Mandatory' -and
            $validationAttributes[0].NamedArguments[0].Argument -is
                [System.Management.Automation.Language.ConstantExpressionAst] -and
            $validationAttributes[0].NamedArguments[0].Argument.Value -eq $true
        ) "$Label $ParameterName must remain a mandatory switch."
    }
    else {
        Assert-True ($validationAttributes.Count -eq 0) `
            "$Label $ParameterName bypass must not have validation attributes."
    }

    $runtimeAssignments = @($ScriptAst.EndBlock.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -ieq $ParameterName
    }, $true) | Where-Object {
        $null -eq (Get-LhmNearestFunctionDefinitionAst -Node $_)
    })
    Assert-True ($runtimeAssignments.Count -eq 0) `
        "$Label must not reassign the $ParameterName bypass at runtime."
}

function Assert-LhmSafeScriptBindingContract {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst,

        [Parameter(Mandatory)]
        [string] $Label,

        [switch] $AllowNoParamBlock
    )

    Assert-True (@($ScriptAst.UsingStatements).Count -eq 0) `
        "$Label must not contain a using statement before identity verification."
    Assert-True ($null -eq $ScriptAst.ScriptRequirements) `
        "$Label must not contain a script requirement before identity verification."

    $nonEndBlocks = @(
        @{ Name = 'begin'; Value = $ScriptAst.BeginBlock },
        @{ Name = 'process'; Value = $ScriptAst.ProcessBlock },
        @{ Name = 'dynamicparam'; Value = $ScriptAst.DynamicParamBlock }
    )
    $cleanBlockProperty = $ScriptAst.PSObject.Properties['CleanBlock']
    if ($null -ne $cleanBlockProperty) {
        $nonEndBlocks += @{ Name = 'clean'; Value = $cleanBlockProperty.Value }
    }
    foreach ($block in $nonEndBlocks) {
        Assert-True ($null -eq $block.Value) (
            "$Label must not contain a non-End execution block '$($block.Name)'."
        )
    }
    Assert-True ($null -ne $ScriptAst.EndBlock) `
        "$Label must contain one End execution block."

    if ($null -eq $ScriptAst.ParamBlock) {
        Assert-True $AllowNoParamBlock `
            "$Label must declare an inert CmdletBinding parameter block."
        return
    }

    $scriptAttributes = @($ScriptAst.ParamBlock.Attributes)
    Assert-True (
        $scriptAttributes.Count -eq 1 -and
        $scriptAttributes[0] -is [System.Management.Automation.Language.AttributeAst] -and
        $scriptAttributes[0].TypeName.FullName -ceq 'CmdletBinding' -and
        $scriptAttributes[0].PositionalArguments.Count -eq 0
    ) "$Label parameter binding must use only [CmdletBinding()]."
    $cmdletBindingArguments = @($scriptAttributes[0].NamedArguments)
    Assert-True (
        $cmdletBindingArguments.Count -eq 0 -or
        ($cmdletBindingArguments.Count -eq 2 -and
            @($cmdletBindingArguments | Where-Object {
                $_.ArgumentName -ceq 'SupportsShouldProcess' -and
                $_.Argument -is
                    [System.Management.Automation.Language.ConstantExpressionAst] -and
                $_.Argument.Value -eq $true
            }).Count -eq 1 -and
            @($cmdletBindingArguments | Where-Object {
                $_.ArgumentName -ceq 'ConfirmImpact' -and
                $_.Argument -is
                    [System.Management.Automation.Language.StringConstantExpressionAst] -and
                $_.Argument.Value -ceq 'High'
            }).Count -eq 1)
    ) "$Label parameter binding contains unapproved CmdletBinding arguments."

    $allowedAttributeNames = @(
        'Parameter',
        'ValidateNotNullOrEmpty',
        'ValidateRange',
        'ValidateSet'
    )
    $allowedTypeNames = @('int', 'string', 'switch', 'uri')
    foreach ($parameter in @($ScriptAst.ParamBlock.Parameters)) {
        Assert-True (
            $parameter.Name.Extent.Text -match '^\$[A-Za-z_][A-Za-z0-9_]*$'
        ) "$Label parameter binding contains a scoped or invalid parameter name."

        foreach ($attribute in @($parameter.Attributes)) {
            if ($attribute -is [System.Management.Automation.Language.TypeConstraintAst]) {
                Assert-True ($allowedTypeNames -contains $attribute.TypeName.FullName) (
                    "$Label parameter binding contains type '$($attribute.TypeName.FullName)'."
                )
                continue
            }

            Assert-True (
                $attribute -is [System.Management.Automation.Language.AttributeAst] -and
                $allowedAttributeNames -contains $attribute.TypeName.FullName
            ) "$Label parameter binding contains an unapproved validation attribute."

            $allowedNamedArguments = if (
                $attribute.TypeName.FullName -ceq 'Parameter'
            ) {
                @('Mandatory')
            }
            else {
                @()
            }
            foreach ($namedArgument in @($attribute.NamedArguments)) {
                Assert-True (
                    $allowedNamedArguments -contains $namedArgument.ArgumentName -and
                    $namedArgument.Argument -is
                        [System.Management.Automation.Language.ConstantExpressionAst]
                ) "$Label parameter binding contains a dynamic or unapproved attribute argument."
            }
            foreach ($positionalArgument in @($attribute.PositionalArguments)) {
                Assert-True (
                    $positionalArgument -is
                        [System.Management.Automation.Language.ConstantExpressionAst] -or
                    $positionalArgument -is
                        [System.Management.Automation.Language.StringConstantExpressionAst]
                ) "$Label parameter binding contains a dynamic attribute argument."
            }
        }

        if ($null -ne $parameter.DefaultValue) {
            Assert-True (
                $parameter.DefaultValue -is
                    [System.Management.Automation.Language.ConstantExpressionAst] -or
                $parameter.DefaultValue -is
                    [System.Management.Automation.Language.StringConstantExpressionAst]
            ) "$Label parameter default must be a literal constant."
        }
    }

    $bindingEffects = @($ScriptAst.ParamBlock.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -or
            $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -or
            $node -is [System.Management.Automation.Language.AssignmentStatementAst] -or
            $node -is [System.Management.Automation.Language.RedirectionAst] -or
            ($node -is [System.Management.Automation.Language.UnaryExpressionAst] -and
                $node.TokenKind.ToString() -match 'PlusPlus|MinusMinus')
    }, $true))
    Assert-True ($bindingEffects.Count -eq 0) `
        "$Label parameter binding contains executable or mutating syntax."
}

function Assert-LhmReadOnlyAst {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.Ast] $Root,

        [Parameter(Mandatory)]
        [hashtable] $FunctionMap,

        [Parameter(Mandatory)]
        [string] $Label,

        [string[]] $AllowedCommands = @(),

        [hashtable] $AllowedCommandParameters = @{},

        [string[]] $ScriptBlockOnlyCommands = @(),

        [string[]] $AllowedMemberCalls = @(),

        [string[]] $AllowedDynamicCommands = @(),

        [AllowNull()]
        [System.Management.Automation.Language.FunctionDefinitionAst] $OwnerFunction,

        [hashtable] $VisitedFunctions = @{}
    )

    if ($null -ne $OwnerFunction -and $null -ne $OwnerFunction.Body.ParamBlock) {
        $functionParamBlock = $OwnerFunction.Body.ParamBlock
        $functionAttributes = @($functionParamBlock.Attributes)
        Assert-True (
            $functionAttributes.Count -eq 0 -or
            ($functionAttributes.Count -eq 1 -and
                $functionAttributes[0] -is
                    [System.Management.Automation.Language.AttributeAst] -and
                $functionAttributes[0].TypeName.FullName -ceq 'CmdletBinding' -and
                $functionAttributes[0].PositionalArguments.Count -eq 0 -and
                $functionAttributes[0].NamedArguments.Count -eq 0)
        ) "$Label function parameter binding contains an unapproved script attribute."

        foreach ($parameter in @($functionParamBlock.Parameters)) {
            Assert-True (
                $parameter.Name.Extent.Text -match '^\$[A-Za-z_][A-Za-z0-9_]*$'
            ) "$Label function parameter binding contains a scoped or invalid name."
            foreach ($attribute in @($parameter.Attributes)) {
                if ($attribute -is [System.Management.Automation.Language.TypeConstraintAst]) {
                    Assert-True (
                        @('int', 'string', 'switch', 'uri') -contains
                            $attribute.TypeName.FullName
                    ) "$Label function parameter binding contains an unapproved type."
                    continue
                }

                Assert-True (
                    $attribute -is [System.Management.Automation.Language.AttributeAst] -and
                    @(
                        'Parameter',
                        'ValidateNotNullOrEmpty',
                        'ValidateRange',
                        'ValidateSet'
                    ) -contains $attribute.TypeName.FullName
                ) "$Label function parameter binding contains an unapproved attribute."
                $allowedNamedArguments = if (
                    $attribute.TypeName.FullName -ceq 'Parameter'
                ) {
                    @('Mandatory')
                }
                else {
                    @()
                }
                foreach ($namedArgument in @($attribute.NamedArguments)) {
                    Assert-True (
                        $allowedNamedArguments -contains $namedArgument.ArgumentName -and
                        $namedArgument.Argument -is
                            [System.Management.Automation.Language.ConstantExpressionAst]
                    ) "$Label function parameter binding contains a dynamic argument."
                }
                foreach ($positionalArgument in @($attribute.PositionalArguments)) {
                    Assert-True (
                        $positionalArgument -is
                            [System.Management.Automation.Language.ConstantExpressionAst] -or
                        $positionalArgument -is
                            [System.Management.Automation.Language.StringConstantExpressionAst]
                    ) "$Label function parameter binding contains a dynamic argument."
                }
            }

            if ($null -ne $parameter.DefaultValue) {
                Assert-True (
                    $parameter.DefaultValue -is
                        [System.Management.Automation.Language.ConstantExpressionAst] -or
                    $parameter.DefaultValue -is
                        [System.Management.Automation.Language.StringConstantExpressionAst]
                ) "$Label function parameter default must be a literal constant."
            }
        }

        $functionBindingEffects = @($functionParamBlock.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -or
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -or
                $node -is [System.Management.Automation.Language.AssignmentStatementAst] -or
                $node -is [System.Management.Automation.Language.RedirectionAst] -or
                ($node -is [System.Management.Automation.Language.UnaryExpressionAst] -and
                    $node.TokenKind.ToString() -match 'PlusPlus|MinusMinus')
        }, $true))
        Assert-True ($functionBindingEffects.Count -eq 0) `
            "$Label function parameter binding contains executable or mutating syntax."
    }

    $belongsToRoot = {
        param($node)
        $nearestFunction = Get-LhmNearestFunctionDefinitionAst -Node $node
        if ($null -eq $OwnerFunction) {
            return $null -eq $nearestFunction
        }
        return [object]::ReferenceEquals($nearestFunction, $OwnerFunction)
    }

    $nestedFunctions = @($Root.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
    }, $true) | Where-Object { & $belongsToRoot $_ })
    Assert-True ($nestedFunctions.Count -eq 0) `
        "$Label contains a nested function definition in its read-only graph."

    foreach ($assignment in @($Root.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst]
    }, $true))) {
        if (-not (& $belongsToRoot $assignment)) {
            continue
        }
        Assert-True (
            $assignment.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $assignment.Left.Extent.Text -match '^\$[A-Za-z_][A-Za-z0-9_]*$'
        ) "$Label contains a non-local assignment '$($assignment.Extent.Text)'."
    }

    $mutatingUnaryExpressions = @($Root.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.UnaryExpressionAst] -and
            $node.TokenKind.ToString() -match 'PlusPlus|MinusMinus'
    }, $true) | Where-Object { & $belongsToRoot $_ })
    Assert-True ($mutatingUnaryExpressions.Count -eq 0) `
        "$Label contains an increment or decrement mutation."

    $redirections = @($Root.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.RedirectionAst]
    }, $true) | Where-Object { & $belongsToRoot $_ })
    Assert-True ($redirections.Count -eq 0) `
        "$Label contains a file or stream redirection."

    foreach ($command in @($Root.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst]
    }, $true))) {
        if (-not (& $belongsToRoot $command)) {
            continue
        }

        $commandName = $command.GetCommandName()
        if ([string]::IsNullOrWhiteSpace($commandName)) {
            $dynamicTarget = if ($command.CommandElements.Count -gt 0) {
                $command.CommandElements[0].Extent.Text.Trim()
            }
            else {
                '<missing>'
            }
            Assert-True ($AllowedDynamicCommands -contains $dynamicTarget) (
                "$Label contains dynamic or unresolved command '$dynamicTarget'."
            )
            Assert-True (
                $command.InvocationOperator -eq
                    [System.Management.Automation.Language.TokenKind]::Ampersand
            ) "$Label dynamic command '$dynamicTarget' must use the call operator."
            Assert-True ($command.CommandElements.Count -eq 1) (
                "$Label dynamic command '$dynamicTarget' must be invoked without arguments."
            )
            Assert-LhmStandalonePipelineCommand `
                -CommandAst $command `
                -Label "$Label dynamic command '$dynamicTarget'"
            continue
        }

        $splattedArguments = @($command.CommandElements | Where-Object {
            $_ -is [System.Management.Automation.Language.VariableExpressionAst] -and
                $_.Splatted
        })
        Assert-True ($splattedArguments.Count -eq 0) (
            "$Label contains splatted arguments for command '$commandName'."
        )

        $allowedParameters = if ($AllowedCommandParameters.ContainsKey($commandName)) {
            @($AllowedCommandParameters[$commandName])
        }
        else {
            @()
        }
        foreach ($commandParameter in @($command.CommandElements | Where-Object {
            $_ -is [System.Management.Automation.Language.CommandParameterAst]
        })) {
            Assert-True (
                $allowedParameters -contains $commandParameter.ParameterName
            ) (
                "$Label contains unapproved parameter " +
                "'-$($commandParameter.ParameterName)' for command '$commandName'."
            )
        }

        if ($ScriptBlockOnlyCommands -contains $commandName) {
            $positionalArguments = @($command.CommandElements | Select-Object -Skip 1)
            Assert-True (
                $positionalArguments.Count -gt 0 -and
                @($positionalArguments | Where-Object {
                    $_ -isnot
                        [System.Management.Automation.Language.ScriptBlockExpressionAst]
                }).Count -eq 0
            ) "$Label command '$commandName' must use positional script blocks only."
        }

        if ($FunctionMap.ContainsKey($commandName)) {
            if (-not $VisitedFunctions.ContainsKey($commandName)) {
                $VisitedFunctions[$commandName] = $true
                $functionDefinition = $FunctionMap[$commandName]
                Assert-LhmReadOnlyAst `
                    -Root $functionDefinition.Body `
                    -FunctionMap $FunctionMap `
                    -Label "$Label -> $commandName" `
                    -AllowedCommands $AllowedCommands `
                    -AllowedCommandParameters $AllowedCommandParameters `
                    -ScriptBlockOnlyCommands $ScriptBlockOnlyCommands `
                    -AllowedMemberCalls $AllowedMemberCalls `
                    -AllowedDynamicCommands $AllowedDynamicCommands `
                    -OwnerFunction $functionDefinition `
                    -VisitedFunctions $VisitedFunctions
            }
            continue
        }

        Assert-True ($AllowedCommands -contains $commandName) (
            "$Label contains command '$commandName', which is not proven read-only."
        )
    }

    foreach ($memberCall in @($Root.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst]
    }, $true))) {
        if (-not (& $belongsToRoot $memberCall)) {
            continue
        }
        $memberName = $memberCall.Member.Extent.Text.Trim("'`"")
        $memberKey = "$($memberCall.Expression.Extent.Text.Trim())::$memberName"
        Assert-True ($AllowedMemberCalls -contains $memberKey) (
            "$Label contains member invocation '$memberKey', which is not proven read-only."
        )
    }
}

function Assert-LhmNoRuntimeTrap {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst,

        [Parameter(Mandatory)]
        [string] $Label
    )

    $runtimeTraps = @($ScriptAst.EndBlock.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.TrapStatementAst]
    }, $true) | Where-Object {
        $null -eq (Get-LhmNearestFunctionDefinitionAst -Node $_)
    })
    Assert-True ($runtimeTraps.Count -eq 0) `
        "$Label must not define a runtime trap that can swallow identity failure."
}

function Assert-LhmCommonIdentityContract {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst,

        [Parameter(Mandatory)]
        [string] $Label
    )

    Assert-LhmSafeScriptBindingContract `
        -ScriptAst $ScriptAst `
        -Label $Label `
        -AllowNoParamBlock
    $statements = @($ScriptAst.EndBlock.Statements)
    Assert-True ($statements.Count -gt 20) "$Label is unexpectedly incomplete."
    Assert-True (
        $statements[0] -is [System.Management.Automation.Language.PipelineAst] -and
        $statements[0].Extent.Text.Trim() -ceq 'Set-StrictMode -Version Latest'
    ) "$Label must begin with Set-StrictMode only."

    foreach ($statement in $statements[1..($statements.Count - 1)]) {
        if ($statement -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
            continue
        }
        Assert-True (
            $statement -is [System.Management.Automation.Language.AssignmentStatementAst]
        ) "$Label dot-source path contains executable top-level code."
        Assert-True (
            $statement.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $statement.Left.Extent.Text -match '^\$script:Lhm[A-Za-z0-9_]+$'
        ) "$Label dot-source path assigns outside its constant script metadata."
        $assignments = @($statement.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.AssignmentStatementAst]
        }, $true))
        Assert-True (
            $assignments.Count -eq 1 -and
            [object]::ReferenceEquals($assignments[0], $statement)
        ) "$Label top-level metadata contains a nested assignment."
        $mutatingUnaryExpressions = @($statement.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.UnaryExpressionAst] -and
                $node.TokenKind.ToString() -match 'PlusPlus|MinusMinus'
        }, $true))
        Assert-True ($mutatingUnaryExpressions.Count -eq 0) `
            "$Label top-level metadata contains increment or decrement mutation."
        $redirections = @($statement.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.RedirectionAst]
        }, $true))
        Assert-True ($redirections.Count -eq 0) `
            "$Label top-level metadata contains a redirection."
        $commands = @($statement.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -or
                $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst]
        }, $true))
        $isVerifierAssignment =
            $statement.Left.VariablePath.UserPath -ieq
                'script:LhmIdentityVerifierPath'
        if (-not $isVerifierAssignment) {
            Assert-True ($commands.Count -eq 0) `
                "$Label top-level assignment invokes executable code."
        }
    }

    Assert-LhmPinnedTopLevelLiteral `
        -ScriptAst $ScriptAst `
        -VariableText '$script:LhmExpectedMachineId' `
        -ExpectedValue 'snd-desk' `
        -Label $Label
    Assert-LhmPinnedTopLevelLiteral `
        -ScriptAst $ScriptAst `
        -VariableText '$script:LhmExpectedInstanceId' `
        -ExpectedValue 'ca96d510-7d87-4cec-8e1a-bd8fc3866903' `
        -Label $Label
    Assert-LhmKnownFolderVerifierAssignment `
        -ScriptAst $ScriptAst `
        -VariableText '$script:LhmIdentityVerifierPath' `
        -Label $Label

    $functionMap = Get-LhmTopLevelFunctionMap -ScriptAst $ScriptAst
    Assert-True ($functionMap.ContainsKey('Assert-LhmVerifiedMachineIdentity')) `
        "$Label does not define Assert-LhmVerifiedMachineIdentity."
    $identityFunction = $functionMap['Assert-LhmVerifiedMachineIdentity']
    Assert-LhmReadOnlyAst `
        -Root $identityFunction.Body `
        -FunctionMap $functionMap `
        -Label "$Label identity function" `
        -AllowedCommands @('Test-Path') `
        -AllowedCommandParameters @{
            'Test-Path' = @('LiteralPath', 'PathType')
        } `
        -AllowedDynamicCommands @('$script:LhmIdentityVerifierPath') `
        -OwnerFunction $identityFunction
}

function Assert-LhmCommonPrelude {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst,

        [Parameter(Mandatory)]
        [int] $GateIndex,

        [Parameter(Mandatory)]
        [string] $Label,

        [string[]] $AllowedFunctionNames = @()
    )

    $statements = @($ScriptAst.EndBlock.Statements)
    Assert-True ($GateIndex -ge 3) "$Label identity gate appears before its safe prelude."
    Assert-True (
        $statements[0].Extent.Text.Trim() -ceq 'Set-StrictMode -Version Latest' -and
        $statements[1] -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $statements[1].Extent.Text.Trim() -ceq "`$ErrorActionPreference = 'Stop'" -and
        $statements[2] -is [System.Management.Automation.Language.PipelineAst] -and
        $statements[2].Extent.Text.Trim() -ceq
            ". (Join-Path `$PSScriptRoot 'LhmLocalRelease.Common.ps1')"
    ) "$Label pre-identity prelude changed from strict mode, error policy, and common definitions."

    $preGateFunctionNames = @()
    for ($index = 3; $index -lt $GateIndex; $index++) {
        Assert-True (
            $statements[$index] -is
                [System.Management.Automation.Language.FunctionDefinitionAst]
        ) "$Label executes code before the production identity gate."
        $functionName = $statements[$index].Name
        Assert-True ($AllowedFunctionNames -contains $functionName) (
            "$Label contains unapproved pre-gate function '$functionName'."
        )
        $preGateFunctionNames += $functionName
    }
    Assert-True (
        $preGateFunctionNames.Count -eq $AllowedFunctionNames.Count -and
        @($AllowedFunctionNames | Where-Object {
            $preGateFunctionNames -notcontains $_
        }).Count -eq 0
    ) "$Label pre-gate function allowlist changed."
    Assert-True (
        @($preGateFunctionNames | Select-Object -Unique).Count -eq
            $preGateFunctionNames.Count
    ) "$Label contains a duplicate pre-gate function definition."
}

function Assert-LhmEntryPointEnvelope {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst,

        [Parameter(Mandatory)]
        [string] $Label
    )

    Assert-LhmSafeScriptBindingContract -ScriptAst $ScriptAst -Label $Label
    Assert-LhmNoRuntimeTrap -ScriptAst $ScriptAst -Label $Label
}

function Assert-LhmLiveIdentityGate {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst,

        [Parameter(Mandatory)]
        [string] $Label
    )

    Assert-LhmEntryPointEnvelope -ScriptAst $ScriptAst -Label $Label
    Assert-LhmInertBypassSwitch `
        -ScriptAst $ScriptAst `
        -ParameterName 'NonLiveTestMode' `
        -Label $Label
    $statements = @($ScriptAst.EndBlock.Statements)
    $gates = @($statements | Where-Object {
        $_ -is [System.Management.Automation.Language.IfStatementAst] -and
            $_.Clauses.Count -eq 1 -and
            $_.Clauses[0].Item1.Extent.Text.Trim() -ceq '-not $NonLiveTestMode'
    })
    Assert-True ($gates.Count -eq 1) `
        "$Label must have one explicit NonLiveTestMode identity bypass."
    $gate = $gates[0]
    Assert-True ($null -eq $gate.ElseClause) `
        "$Label production identity gate must not have an else branch."

    $gateIndex = -1
    for ($index = 0; $index -lt $statements.Count; $index++) {
        if ([object]::ReferenceEquals($statements[$index], $gate)) {
            $gateIndex = $index
            break
        }
    }
    Assert-True ($gateIndex -ge 0) "$Label identity gate is not top-level."
    Assert-LhmCommonPrelude `
        -ScriptAst $ScriptAst `
        -GateIndex $gateIndex `
        -Label $Label `
        -AllowedFunctionNames @('Invoke-TestFailurePoint')

    $gateStatements = @($gate.Clauses[0].Item2.Statements)
    Assert-True (
        $gateStatements.Count -eq 1 -and
        $gateStatements[0] -is
            [System.Management.Automation.Language.AssignmentStatementAst]
    ) "$Label identity gate must contain one direct assignment."
    $gateCommands = @($gateStatements[0].FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst]
    }, $true))
    Assert-True (
        $gateCommands.Count -eq 1
    ) "$Label identity gate must contain exactly one command."
    Assert-LhmBareIdentityCommand `
        -CommandAst $gateCommands[0] `
        -ExpectedName 'Assert-LhmVerifiedMachineIdentity' `
        -Label "$Label identity gate"
    Assert-LhmCanonicalIdentityAssignment `
        -AssignmentAst $gateStatements[0] `
        -CommandAst $gateCommands[0] `
        -Label "$Label identity gate"

    $runtimeIdentityCalls = @($ScriptAst.EndBlock.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -ceq 'Assert-LhmVerifiedMachineIdentity'
    }, $true) | Where-Object {
        $null -eq (Get-LhmNearestFunctionDefinitionAst -Node $_)
    })
    Assert-True ($runtimeIdentityCalls.Count -eq 1) `
        "$Label must call Assert-LhmVerifiedMachineIdentity exactly once at runtime."
}

function Assert-LhmFinalizeIdentityGate {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst,

        [Parameter(Mandatory)]
        [string] $Label
    )

    Assert-LhmEntryPointEnvelope -ScriptAst $ScriptAst -Label $Label
    Assert-LhmInertBypassSwitch `
        -ScriptAst $ScriptAst `
        -ParameterName 'AttendedUiAccepted' `
        -Label $Label `
        -RequireMandatory
    Assert-LhmInertBypassSwitch `
        -ScriptAst $ScriptAst `
        -ParameterName 'NormalUserLauncherAccepted' `
        -Label $Label `
        -RequireMandatory
    $statements = @($ScriptAst.EndBlock.Statements)
    Assert-LhmCommonPrelude -ScriptAst $ScriptAst -GateIndex 3 -Label $Label
    $expectedGuards = @(
        @{
            Condition = '-not $AttendedUiAccepted'
            Message =
                'Use -AttendedUiAccepted only after checking the populated restored UI.'
        },
        @{
            Condition = '-not $NormalUserLauncherAccepted'
            Message =
                'Use -NormalUserLauncherAccepted only after invoking librehw.cmd from a normal unelevated shell.'
        }
    )
    for ($offset = 0; $offset -lt $expectedGuards.Count; $offset++) {
        $guard = $statements[3 + $offset]
        Assert-True (
            $guard -is [System.Management.Automation.Language.IfStatementAst] -and
            $guard.Clauses.Count -eq 1 -and
            $null -eq $guard.ElseClause -and
            $guard.Clauses[0].Item1.Extent.Text.Trim() -ceq
                $expectedGuards[$offset].Condition -and
            $guard.Clauses[0].Item2.Statements.Count -eq 1 -and
            $guard.Clauses[0].Item2.Statements[0] -is
                [System.Management.Automation.Language.ThrowStatementAst]
        ) "$Label acceptance guard changed before identity verification."
        $throwStatement = $guard.Clauses[0].Item2.Statements[0]
        Assert-LhmLiteralThrowStatement `
            -ThrowAst $throwStatement `
            -ExpectedMessage $expectedGuards[$offset].Message `
            -Label "$Label acceptance guard"
    }

    $identityStatement = $statements[5]
    $identityCommands = @($identityStatement.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst]
    }, $true))
    Assert-True (
        $identityStatement -is
            [System.Management.Automation.Language.AssignmentStatementAst] -and
        $identityCommands.Count -eq 1
    ) "$Label identity verification must immediately follow its two acceptance guards."
    Assert-LhmBareIdentityCommand `
        -CommandAst $identityCommands[0] `
        -ExpectedName 'Assert-LhmVerifiedMachineIdentity' `
        -Label "$Label identity gate"
    Assert-LhmCanonicalIdentityAssignment `
        -AssignmentAst $identityStatement `
        -CommandAst $identityCommands[0] `
        -Label "$Label identity gate"
    $runtimeIdentityCalls = @($ScriptAst.EndBlock.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -ceq 'Assert-LhmVerifiedMachineIdentity'
    }, $true) | Where-Object {
        $null -eq (Get-LhmNearestFunctionDefinitionAst -Node $_)
    })
    Assert-True ($runtimeIdentityCalls.Count -eq 1) `
        "$Label must call Assert-LhmVerifiedMachineIdentity exactly once at runtime."
}

function Assert-LhmRetiredIdentityGate {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst,

        [Parameter(Mandatory)]
        [string] $Label
    )

    Assert-LhmEntryPointEnvelope -ScriptAst $ScriptAst -Label $Label
    $statements = @($ScriptAst.EndBlock.Statements)
    Assert-True ($statements.Count -eq 5) `
        "$Label retired entry point must contain exactly five top-level statements."
    Assert-LhmCommonPrelude -ScriptAst $ScriptAst -GateIndex 3 -Label $Label
    $identityCommands = @($statements[3].FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst]
    }, $true))
    Assert-True (
        $statements[3] -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $identityCommands.Count -eq 1 -and
        $statements[4] -is [System.Management.Automation.Language.ThrowStatementAst]
    ) "$Label retired entry point must verify identity and then terminate."
    Assert-LhmBareIdentityCommand `
        -CommandAst $identityCommands[0] `
        -ExpectedName 'Assert-LhmVerifiedMachineIdentity' `
        -Label "$Label identity gate"
    Assert-LhmCanonicalIdentityAssignment `
        -AssignmentAst $statements[3] `
        -CommandAst $identityCommands[0] `
        -Label "$Label identity gate"
    Assert-LhmLiteralThrowStatement `
        -ThrowAst $statements[4] `
        -Label "$Label retired terminal throw"
}

function Assert-LhmLauncherIdentityGate {
    param(
        [Parameter(Mandatory)]
        [System.Management.Automation.Language.ScriptBlockAst] $ScriptAst,

        [Parameter(Mandatory)]
        [string] $Label
    )

    Assert-LhmEntryPointEnvelope -ScriptAst $ScriptAst -Label $Label
    Assert-LhmInertBypassSwitch `
        -ScriptAst $ScriptAst `
        -ParameterName 'ValidateScriptOnly' `
        -Label $Label
    $statements = @($ScriptAst.EndBlock.Statements)
    Assert-LhmPinnedTopLevelLiteral `
        -ScriptAst $ScriptAst `
        -VariableText '$ExpectedMachineId' `
        -ExpectedValue 'snd-desk' `
        -Label $Label
    Assert-LhmPinnedTopLevelLiteral `
        -ScriptAst $ScriptAst `
        -VariableText '$ExpectedInstanceId' `
        -ExpectedValue 'ca96d510-7d87-4cec-8e1a-bd8fc3866903' `
        -Label $Label
    Assert-LhmKnownFolderVerifierAssignment `
        -ScriptAst $ScriptAst `
        -VariableText '$IdentityVerifierPath' `
        -Label $Label
    $functionMap = Get-LhmTopLevelFunctionMap -ScriptAst $ScriptAst
    Assert-True ($functionMap.ContainsKey('Assert-LauncherMachineIdentity')) `
        "$Label does not define Assert-LauncherMachineIdentity."
    $identityFunction = $functionMap['Assert-LauncherMachineIdentity']
    Assert-LhmReadOnlyAst `
        -Root $identityFunction.Body `
        -FunctionMap $functionMap `
        -Label "$Label identity function" `
        -AllowedCommands @('Test-Path') `
        -AllowedCommandParameters @{
            'Test-Path' = @('LiteralPath', 'PathType')
        } `
        -AllowedDynamicCommands @('$IdentityVerifierPath') `
        -OwnerFunction $identityFunction

    $validationBranches = @($statements | Where-Object {
        $_ -is [System.Management.Automation.Language.IfStatementAst] -and
            $_.Clauses.Count -eq 1 -and
            $_.Clauses[0].Item1.Extent.Text.Trim() -ceq '$ValidateScriptOnly'
    })
    Assert-True ($validationBranches.Count -eq 1) `
        "$Label must have one top-level ValidateScriptOnly branch."
    $validationBranch = $validationBranches[0]
    Assert-True ($null -eq $validationBranch.ElseClause) `
        "$Label ValidateScriptOnly branch must not have an else branch."

    $validationIndex = -1
    for ($index = 0; $index -lt $statements.Count; $index++) {
        if ([object]::ReferenceEquals($statements[$index], $validationBranch)) {
            $validationIndex = $index
            break
        }
    }
    Assert-True ($validationIndex -ge 2) "$Label validation branch is not top-level."
    for ($index = 0; $index -lt $validationIndex; $index++) {
        $statement = $statements[$index]
        if ($statement -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
            continue
        }
        Assert-True (
            $statement -is [System.Management.Automation.Language.PipelineAst] -or
            ($statement -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $statement.Left -is [System.Management.Automation.Language.VariableExpressionAst])
        ) "$Label executes a control-flow statement before validation and identity."
        Assert-LhmReadOnlyAst `
            -Root $statement `
            -FunctionMap $functionMap `
            -Label "$Label pre-identity statement $index" `
            -AllowedCommands @('Set-StrictMode') `
            -AllowedCommandParameters @{
                'Set-StrictMode' = @('Version')
            } `
            -AllowedMemberCalls @(
                '[System.IO.Path]::Combine',
                '[Environment]::GetFolderPath'
            )
    }

    $validationStatements = @($validationBranch.Clauses[0].Item2.Statements)
    Assert-True (
        $validationStatements.Count -gt 0 -and
        $validationStatements[-1] -is
            [System.Management.Automation.Language.ReturnStatementAst]
    ) "$Label ValidateScriptOnly branch must terminate with return."
    Assert-LhmReadOnlyAst `
        -Root $validationBranch.Clauses[0].Item2 `
        -FunctionMap $functionMap `
        -Label "$Label ValidateScriptOnly graph" `
        -AllowedCommands @('ForEach-Object', 'Get-Process', 'Where-Object') `
        -AllowedCommandParameters @{
            'ForEach-Object' = @()
            'Get-LibreHardwareMonitorProcess' = @('InspectionOnly')
            'Get-Process' = @('Name', 'ErrorAction')
            'Test-LauncherPathEqual' = @('Left', 'Right')
            'Where-Object' = @()
        } `
        -ScriptBlockOnlyCommands @('ForEach-Object', 'Where-Object') `
        -AllowedMemberCalls @(
            '[string]::Equals',
            '[System.IO.Path]::GetFullPath'
        )

    Assert-True ($validationIndex + 1 -lt $statements.Count) `
        "$Label has no production identity statement after validation."
    $identityStatement = $statements[$validationIndex + 1]
    $identityCommands = @($identityStatement.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst]
    }, $true))
    Assert-True (
        $identityStatement -is [System.Management.Automation.Language.PipelineAst] -and
        $identityCommands.Count -eq 1
    ) "$Label identity must be the immediate executable statement after validation."
    Assert-LhmBareIdentityCommand `
        -CommandAst $identityCommands[0] `
        -ExpectedName 'Assert-LauncherMachineIdentity' `
        -Label "$Label identity gate"
    Assert-True (
        [object]::ReferenceEquals($identityStatement, $identityCommands[0].Parent)
    ) "$Label identity command must be the complete identity statement."

    $runtimeIdentityCalls = @($ScriptAst.EndBlock.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -ceq 'Assert-LauncherMachineIdentity'
    }, $true) | Where-Object {
        $null -eq (Get-LhmNearestFunctionDefinitionAst -Node $_)
    })
    Assert-True ($runtimeIdentityCalls.Count -eq 1) `
        "$Label must call Assert-LauncherMachineIdentity exactly once at runtime."
}

function Invoke-LhmIdentityContractPreflight {
    Assert-LhmCommonIdentityContract `
        -ScriptAst (Get-LhmScriptAst -Path $commonScript) `
        -Label 'LhmLocalRelease.Common.ps1'
    Assert-LhmLiveIdentityGate `
        -ScriptAst (Get-LhmScriptAst -Path $installScript) `
        -Label 'Install-LibreHardwareMonitorRelease.ps1'
    Assert-LhmLiveIdentityGate `
        -ScriptAst (Get-LhmScriptAst -Path $rollbackScript) `
        -Label 'Restore-LibreHardwareMonitorRelease.ps1'
    Assert-LhmLiveIdentityGate `
        -ScriptAst (Get-LhmScriptAst -Path $relocationScript) `
        -Label 'Relocate-LibreHardwareMonitorDataRoot.ps1'
    Assert-LhmLiveIdentityGate `
        -ScriptAst (Get-LhmScriptAst -Path $runtimeMigrationScript) `
        -Label 'Move-LibreHardwareMonitorRuntimeRoot.ps1'
    Assert-LhmFinalizeIdentityGate `
        -ScriptAst (Get-LhmScriptAst -Path $finalizeScript) `
        -Label 'Finalize-LibreHardwareMonitorCutover.ps1'
    Assert-LhmRetiredIdentityGate `
        -ScriptAst (Get-LhmScriptAst -Path $legacyRecoveryScript) `
        -Label 'Restore-LegacyLibreHardwareMonitorStartup.ps1'
    Assert-LhmRetiredIdentityGate `
        -ScriptAst (Get-LhmScriptAst -Path $preStableRecoveryScript) `
        -Label 'Restore-PreStableLibreHardwareMonitorStartup.ps1'
    Assert-LhmLauncherIdentityGate `
        -ScriptAst (Get-LhmScriptAst -Path $canonicalLauncher) `
        -Label 'Start-LibreHardwareMonitor.ps1'

    $noGateTokens = $null
    $noGateErrors = $null
    $noGateAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $NonLiveTestMode)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
function Invoke-TestFailurePoint { }
$mode = Assert-LhmOperationMode -NonLiveTestMode:$NonLiveTestMode
'@, [ref]$noGateTokens, [ref]$noGateErrors)
    Assert-True ($noGateErrors.Count -eq 0) `
        'Removed identity-gate fixture did not parse.'
    Assert-Throws -MessagePattern 'one explicit NonLiveTestMode identity bypass' -Action {
        Assert-LhmLiveIdentityGate `
            -ScriptAst $noGateAst `
            -Label 'removed identity-gate fixture'
    }

    $truthyBypassTokens = $null
    $truthyBypassErrors = $null
    $truthyBypassAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([int] $NonLiveTestMode = 1)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
function Invoke-TestFailurePoint { }
if (-not $NonLiveTestMode) {
    $null = Assert-LhmVerifiedMachineIdentity
}
'@, [ref]$truthyBypassTokens, [ref]$truthyBypassErrors)
    Assert-True ($truthyBypassErrors.Count -eq 0) `
        'Truthy NonLiveTestMode default fixture did not parse.'
    Assert-Throws -MessagePattern 'default-false switch only' -Action {
        Assert-LhmLiveIdentityGate `
            -ScriptAst $truthyBypassAst `
            -Label 'truthy NonLiveTestMode default fixture'
    }

    $noLauncherGateTokens = $null
    $noLauncherGateErrors = $null
    $noLauncherGateAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $ValidateScriptOnly)

$ErrorActionPreference = 'Stop'
$IdentityVerifierPath = [System.IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), 'common_dev\v2\Test-LocalMachineIdentity.ps1')
$ExpectedMachineId = 'snd-desk'
$ExpectedInstanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903'
function Assert-LauncherMachineIdentity { }
if ($ValidateScriptOnly) {
    return
}
Initialize-LauncherNativeMethods
'@, [ref]$noLauncherGateTokens, [ref]$noLauncherGateErrors)
    Assert-True ($noLauncherGateErrors.Count -eq 0) `
        'Removed launcher identity-gate fixture did not parse.'
    Assert-Throws -MessagePattern "bare command 'Assert-LauncherMachineIdentity'" -Action {
        Assert-LhmLauncherIdentityGate `
            -ScriptAst $noLauncherGateAst `
            -Label 'removed launcher identity-gate fixture'
    }

    $truthyValidationTokens = $null
    $truthyValidationErrors = $null
    $truthyValidationAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([int] $ValidateScriptOnly = 1)

$IdentityVerifierPath = [System.IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), 'common_dev\v2\Test-LocalMachineIdentity.ps1')
$ExpectedMachineId = 'snd-desk'
$ExpectedInstanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903'
function Assert-LauncherMachineIdentity { }
if ($ValidateScriptOnly) {
    return
}
Assert-LauncherMachineIdentity
'@, [ref]$truthyValidationTokens, [ref]$truthyValidationErrors)
    Assert-True ($truthyValidationErrors.Count -eq 0) `
        'Truthy ValidateScriptOnly default fixture did not parse.'
    Assert-Throws -MessagePattern 'default-false switch only' -Action {
        Assert-LhmLauncherIdentityGate `
            -ScriptAst $truthyValidationAst `
            -Label 'truthy ValidateScriptOnly default fixture'
    }

    $dynamicValidationTokens = $null
    $dynamicValidationErrors = $null
    $dynamicValidationAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $ValidateScriptOnly)

$IdentityVerifierPath = [System.IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), 'common_dev\v2\Test-LocalMachineIdentity.ps1')
$ExpectedMachineId = 'snd-desk'
$ExpectedInstanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903'
function Assert-LauncherMachineIdentity { }
function Invoke-UnsafeValidation { & $LauncherTargetPath }
if ($ValidateScriptOnly) {
    Invoke-UnsafeValidation
    return
}
Assert-LauncherMachineIdentity
'@, [ref]$dynamicValidationTokens, [ref]$dynamicValidationErrors)
    Assert-True ($dynamicValidationErrors.Count -eq 0) `
        'Dynamic validation-effect fixture did not parse.'
    Assert-Throws -MessagePattern 'dynamic or unresolved command' -Action {
        Assert-LhmLauncherIdentityGate `
            -ScriptAst $dynamicValidationAst `
            -Label 'dynamic validation-effect fixture'
    }

    $memberValidationTokens = $null
    $memberValidationErrors = $null
    $memberValidationAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $ValidateScriptOnly)

$IdentityVerifierPath = [System.IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), 'common_dev\v2\Test-LocalMachineIdentity.ps1')
$ExpectedMachineId = 'snd-desk'
$ExpectedInstanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903'
function Assert-LauncherMachineIdentity { }
function Invoke-UnsafeValidation { $shortcut.Save() }
if ($ValidateScriptOnly) {
    Invoke-UnsafeValidation
    return
}
Assert-LauncherMachineIdentity
'@, [ref]$memberValidationTokens, [ref]$memberValidationErrors)
    Assert-True ($memberValidationErrors.Count -eq 0) `
        'Member validation-effect fixture did not parse.'
    Assert-Throws -MessagePattern 'member invocation' -Action {
        Assert-LhmLauncherIdentityGate `
            -ScriptAst $memberValidationAst `
            -Label 'member validation-effect fixture'
    }

    $nestedFunctionTokens = $null
    $nestedFunctionErrors = $null
    $nestedFunctionAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $ValidateScriptOnly)

$ErrorActionPreference = 'Stop'
$IdentityVerifierPath = [System.IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), 'common_dev\v2\Test-LocalMachineIdentity.ps1')
$ExpectedMachineId = 'snd-desk'
$ExpectedInstanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903'
function Assert-LauncherMachineIdentity { }
if ($ValidateScriptOnly) {
    function Get-Process { Remove-Item 'C:\safety-sentinel' }
    Get-Process
    return
}
Assert-LauncherMachineIdentity
'@, [ref]$nestedFunctionTokens, [ref]$nestedFunctionErrors)
    Assert-True ($nestedFunctionErrors.Count -eq 0) `
        'Nested function-shadow fixture did not parse.'
    Assert-Throws -MessagePattern 'nested function definition' -Action {
        Assert-LhmLauncherIdentityGate `
            -ScriptAst $nestedFunctionAst `
            -Label 'nested function-shadow fixture'
    }

    $shadowedGateTokens = $null
    $shadowedGateErrors = $null
    $shadowedGateAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $NonLiveTestMode)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
function Assert-LhmVerifiedMachineIdentity { }
function Invoke-TestFailurePoint { }
if (-not $NonLiveTestMode) {
    $null = Assert-LhmVerifiedMachineIdentity
}
'@, [ref]$shadowedGateTokens, [ref]$shadowedGateErrors)
    Assert-True ($shadowedGateErrors.Count -eq 0) `
        'Shadowed identity-gate fixture did not parse.'
    Assert-Throws -MessagePattern 'unapproved pre-gate function' -Action {
        Assert-LhmLiveIdentityGate `
            -ScriptAst $shadowedGateAst `
            -Label 'shadowed identity-gate fixture'
    }

    $gateArgumentTokens = $null
    $gateArgumentErrors = $null
    $gateArgumentAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $NonLiveTestMode)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
function Invoke-TestFailurePoint { }
if (-not $NonLiveTestMode) {
    $null = Assert-LhmVerifiedMachineIdentity `
        ([System.IO.File]::WriteAllText('C:\safety-sentinel', 'unsafe'))
}
'@, [ref]$gateArgumentTokens, [ref]$gateArgumentErrors)
    Assert-True ($gateArgumentErrors.Count -eq 0) `
        'Identity-gate argument fixture did not parse.'
    Assert-Throws -MessagePattern 'without arguments' -Action {
        Assert-LhmLiveIdentityGate `
            -ScriptAst $gateArgumentAst `
            -Label 'identity-gate argument fixture'
    }

    $gateRedirectionTokens = $null
    $gateRedirectionErrors = $null
    $gateRedirectionAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $NonLiveTestMode)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
function Invoke-TestFailurePoint { }
if (-not $NonLiveTestMode) {
    $null = Assert-LhmVerifiedMachineIdentity 3> 'C:\safety-sentinel'
}
'@, [ref]$gateRedirectionTokens, [ref]$gateRedirectionErrors)
    Assert-True ($gateRedirectionErrors.Count -eq 0) `
        'Identity-gate redirection fixture did not parse.'
    Assert-Throws -MessagePattern 'without arguments' -Action {
        Assert-LhmLiveIdentityGate `
            -ScriptAst $gateRedirectionAst `
            -Label 'identity-gate redirection fixture'
    }

    $gateSubexpressionTokens = $null
    $gateSubexpressionErrors = $null
    $gateSubexpressionAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $NonLiveTestMode)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
function Invoke-TestFailurePoint { }
if (-not $NonLiveTestMode) {
    $null = $([System.IO.File]::Delete('C:\safety-sentinel');
        Assert-LhmVerifiedMachineIdentity)
}
'@, [ref]$gateSubexpressionTokens, [ref]$gateSubexpressionErrors)
    Assert-True ($gateSubexpressionErrors.Count -eq 0) `
        'Identity-gate subexpression fixture did not parse.'
    Assert-Throws -MessagePattern 'complete RHS' -Action {
        Assert-LhmLiveIdentityGate `
            -ScriptAst $gateSubexpressionAst `
            -Label 'identity-gate subexpression fixture'
    }

    $finalizeGuardTokens = $null
    $finalizeGuardErrors = $null
    $finalizeGuardAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [switch] $AttendedUiAccepted,

    [Parameter(Mandatory)]
    [switch] $NormalUserLauncherAccepted
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
if (-not $AttendedUiAccepted) {
    throw ([System.IO.File]::Delete('C:\safety-sentinel'))
}
if (-not $NormalUserLauncherAccepted) {
    throw 'Use -NormalUserLauncherAccepted only after invoking librehw.cmd from a normal unelevated shell.'
}
$null = Assert-LhmVerifiedMachineIdentity
'@, [ref]$finalizeGuardTokens, [ref]$finalizeGuardErrors)
    Assert-True ($finalizeGuardErrors.Count -eq 0) `
        'Finalize acceptance-guard effect fixture did not parse.'
    Assert-Throws -MessagePattern 'reviewed literal message' -Action {
        Assert-LhmFinalizeIdentityGate `
            -ScriptAst $finalizeGuardAst `
            -Label 'finalize acceptance-guard effect fixture'
    }

    $finalizeTruthyTokens = $null
    $finalizeTruthyErrors = $null
    $finalizeTruthyAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param(
    [int] $AttendedUiAccepted = 1,
    [int] $NormalUserLauncherAccepted = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
if (-not $AttendedUiAccepted) {
    throw 'Use -AttendedUiAccepted only after checking the populated restored UI.'
}
if (-not $NormalUserLauncherAccepted) {
    throw 'Use -NormalUserLauncherAccepted only after invoking librehw.cmd from a normal unelevated shell.'
}
$null = Assert-LhmVerifiedMachineIdentity
'@, [ref]$finalizeTruthyTokens, [ref]$finalizeTruthyErrors)
    Assert-True ($finalizeTruthyErrors.Count -eq 0) `
        'Truthy finalize-acceptance defaults fixture did not parse.'
    Assert-Throws -MessagePattern 'default-false switch only' -Action {
        Assert-LhmFinalizeIdentityGate `
            -ScriptAst $finalizeTruthyAst `
            -Label 'truthy finalize-acceptance defaults fixture'
    }

    $retiredThrowTokens = $null
    $retiredThrowErrors = $null
    $retiredThrowAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
$null = Assert-LhmVerifiedMachineIdentity
throw $(Remove-Item 'C:\safety-sentinel'; 'retired')
'@, [ref]$retiredThrowTokens, [ref]$retiredThrowErrors)
    Assert-True ($retiredThrowErrors.Count -eq 0) `
        'Retired mutating-throw fixture did not parse.'
    Assert-Throws -MessagePattern 'reviewed literal message' -Action {
        Assert-LhmRetiredIdentityGate `
            -ScriptAst $retiredThrowAst `
            -Label 'retired mutating-throw fixture'
    }

    $memberNameTokens = $null
    $memberNameErrors = $null
    $memberNameAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $ValidateScriptOnly)

$IdentityVerifierPath = [System.IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), 'common_dev\v2\Test-LocalMachineIdentity.ps1')
$ExpectedMachineId = 'snd-desk'
$ExpectedInstanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903'
function Assert-LauncherMachineIdentity { }
function Invoke-UnsafeValidation {
    Get-Process | ForEach-Object -MemberName Kill
}
if ($ValidateScriptOnly) {
    Invoke-UnsafeValidation
    return
}
Assert-LauncherMachineIdentity
'@, [ref]$memberNameTokens, [ref]$memberNameErrors)
    Assert-True ($memberNameErrors.Count -eq 0) `
        'ForEach-Object MemberName fixture did not parse.'
    Assert-Throws -MessagePattern "unapproved parameter '-MemberName'" -Action {
        Assert-LhmLauncherIdentityGate `
            -ScriptAst $memberNameAst `
            -Label 'ForEach-Object MemberName fixture'
    }

    $outputVariableTokens = $null
    $outputVariableErrors = $null
    $outputVariableAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $ValidateScriptOnly)

$IdentityVerifierPath = [System.IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), 'common_dev\v2\Test-LocalMachineIdentity.ps1')
$ExpectedMachineId = 'snd-desk'
$ExpectedInstanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903'
function Assert-LauncherMachineIdentity { }
function Invoke-UnsafeValidation {
    Get-Process -OutVariable global:LhmUnsafeOutput
}
if ($ValidateScriptOnly) {
    Invoke-UnsafeValidation
    return
}
Assert-LauncherMachineIdentity
'@, [ref]$outputVariableTokens, [ref]$outputVariableErrors)
    Assert-True ($outputVariableErrors.Count -eq 0) `
        'Scoped output-variable fixture did not parse.'
    Assert-Throws -MessagePattern "unapproved parameter '-OutVariable'" -Action {
        Assert-LhmLauncherIdentityGate `
            -ScriptAst $outputVariableAst `
            -Label 'scoped output-variable fixture'
    }

    $splatTokens = $null
    $splatErrors = $null
    $splatAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $ValidateScriptOnly)

$IdentityVerifierPath = [System.IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), 'common_dev\v2\Test-LocalMachineIdentity.ps1')
$ExpectedMachineId = 'snd-desk'
$ExpectedInstanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903'
function Assert-LauncherMachineIdentity { }
function Invoke-UnsafeValidation {
    $arguments = @{ OutVariable = 'global:LhmUnsafeOutput' }
    Get-Process @arguments
}
if ($ValidateScriptOnly) {
    Invoke-UnsafeValidation
    return
}
Assert-LauncherMachineIdentity
'@, [ref]$splatTokens, [ref]$splatErrors)
    Assert-True ($splatErrors.Count -eq 0) `
        'Splatted validation-argument fixture did not parse.'
    Assert-Throws -MessagePattern 'splatted arguments' -Action {
        Assert-LhmLauncherIdentityGate `
            -ScriptAst $splatAst `
            -Label 'splatted validation-argument fixture'
    }

    $unsafeDefaultTokens = $null
    $unsafeDefaultErrors = $null
    $unsafeDefaultAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param(
    [switch] $NonLiveTestMode,
    [string] $Probe = $(Remove-Item 'C:\safety-sentinel')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
function Invoke-TestFailurePoint { }
if (-not $NonLiveTestMode) {
    $null = Assert-LhmVerifiedMachineIdentity
}
'@, [ref]$unsafeDefaultTokens, [ref]$unsafeDefaultErrors)
    Assert-True ($unsafeDefaultErrors.Count -eq 0) `
        'Unsafe parameter-default fixture did not parse.'
    Assert-Throws -MessagePattern 'parameter default must be a literal constant' -Action {
        Assert-LhmLiveIdentityGate `
            -ScriptAst $unsafeDefaultAst `
            -Label 'unsafe parameter-default fixture'
    }

    $nonEndTokens = $null
    $nonEndErrors = $null
    $nonEndAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
[CmdletBinding()]
param([switch] $NonLiveTestMode)

begin {
    Remove-Item 'C:\safety-sentinel'
}
end {
    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'
    . (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
    function Invoke-TestFailurePoint { }
    if (-not $NonLiveTestMode) {
        $null = Assert-LhmVerifiedMachineIdentity
    }
}
'@, [ref]$nonEndTokens, [ref]$nonEndErrors)
    Assert-True ($nonEndErrors.Count -eq 0) `
        'Non-End execution-block fixture did not parse.'
    Assert-Throws -MessagePattern 'non-End execution block' -Action {
        Assert-LhmLiveIdentityGate `
            -ScriptAst $nonEndAst `
            -Label 'non-End execution-block fixture'
    }

    $usingTokens = $null
    $usingErrors = $null
    $usingAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
using namespace System

[CmdletBinding()]
param([switch] $NonLiveTestMode)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
function Invoke-TestFailurePoint { }
if (-not $NonLiveTestMode) {
    $null = Assert-LhmVerifiedMachineIdentity
}
'@, [ref]$usingTokens, [ref]$usingErrors)
    Assert-True ($usingErrors.Count -eq 0) `
        'Using-statement fixture did not parse.'
    Assert-Throws -MessagePattern 'using statement' -Action {
        Assert-LhmLiveIdentityGate `
            -ScriptAst $usingAst `
            -Label 'using-statement fixture'
    }

    Assert-Throws -MessagePattern 'load-time using module or assembly' -Action {
        Assert-LhmNoLoadTimeDirectiveText `
            -Text 'using <# split token #> module UnsafeModule' `
            -Label 'load-time module fixture'
    }

    $requiresTokens = $null
    $requiresErrors = $null
    $requiresAst = [System.Management.Automation.Language.Parser]::ParseInput(@'
#requires -Version 5.1
[CmdletBinding()]
param([switch] $NonLiveTestMode)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LhmLocalRelease.Common.ps1')
function Invoke-TestFailurePoint { }
if (-not $NonLiveTestMode) {
    $null = Assert-LhmVerifiedMachineIdentity
}
'@, [ref]$requiresTokens, [ref]$requiresErrors)
    Assert-True ($requiresErrors.Count -eq 0) `
        'Script-requirement fixture did not parse.'
    Assert-Throws -MessagePattern 'script requirement' -Action {
        Assert-LhmLiveIdentityGate `
            -ScriptAst $requiresAst `
            -Label 'script-requirement fixture'
    }

    $commonText = [System.IO.File]::ReadAllText($commonScript)
    $unsafeCommonText = $commonText.Replace(
        '$script:LhmExecutableName = ''LibreHardwareMonitor.Windows.Forms.exe''',
        '$script:LhmExecutableName = ' +
            '($global:LhmUnsafeMetadata = ''LibreHardwareMonitor.Windows.Forms.exe'')')
    Assert-True ($unsafeCommonText -cne $commonText) `
        'Nested metadata-assignment fixture replacement did not apply.'
    $unsafeCommonTokens = $null
    $unsafeCommonErrors = $null
    $unsafeCommonAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $unsafeCommonText,
        [ref]$unsafeCommonTokens,
        [ref]$unsafeCommonErrors)
    Assert-True ($unsafeCommonErrors.Count -eq 0) `
        'Nested metadata-assignment fixture did not parse.'
    Assert-Throws -MessagePattern 'nested assignment' -Action {
        Assert-LhmCommonIdentityContract `
            -ScriptAst $unsafeCommonAst `
            -Label 'nested metadata-assignment fixture'
    }

    $unsafeUnaryText = $commonText.Replace(
        '$script:LhmExecutableName = ''LibreHardwareMonitor.Windows.Forms.exe''',
        '$script:LhmExecutableName = ($global:LhmUnsafeMetadata++)')
    Assert-True ($unsafeUnaryText -cne $commonText) `
        'Unary metadata-mutation fixture replacement did not apply.'
    $unsafeUnaryTokens = $null
    $unsafeUnaryErrors = $null
    $unsafeUnaryAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $unsafeUnaryText,
        [ref]$unsafeUnaryTokens,
        [ref]$unsafeUnaryErrors)
    Assert-True ($unsafeUnaryErrors.Count -eq 0) `
        'Unary metadata-mutation fixture did not parse.'
    Assert-Throws -MessagePattern 'increment or decrement mutation' -Action {
        Assert-LhmCommonIdentityContract `
            -ScriptAst $unsafeUnaryAst `
            -Label 'unary metadata-mutation fixture'
    }

    $verifierInvocation = '$identity = & $script:LhmIdentityVerifierPath'
    $unsafeVerifierCalls = @(
        @{
            Name = 'dynamic verifier splat'
            Replacement =
                '$identity = & $script:LhmIdentityVerifierPath @LhmUnsafeArguments'
            Message = 'must be invoked without arguments'
        },
        @{
            Name = 'dynamic verifier positional argument'
            Replacement =
                '$identity = & $script:LhmIdentityVerifierPath ''unsafe'''
            Message = 'must be invoked without arguments'
        },
        @{
            Name = 'dynamic verifier dot-source'
            Replacement = '$identity = . $script:LhmIdentityVerifierPath'
            Message = 'must use the call operator'
        },
        @{
            Name = 'dynamic verifier pipeline input'
            Replacement =
                '$identity = ''unsafe'' | & $script:LhmIdentityVerifierPath'
            Message = 'no pipeline input or output'
        }
    )
    foreach ($unsafeVerifierCall in $unsafeVerifierCalls) {
        $unsafeVerifierText = $commonText.Replace(
            $verifierInvocation,
            $unsafeVerifierCall.Replacement)
        Assert-True ($unsafeVerifierText -cne $commonText) `
            "$($unsafeVerifierCall.Name) fixture replacement did not apply."
        $unsafeVerifierTokens = $null
        $unsafeVerifierErrors = $null
        $unsafeVerifierAst = [System.Management.Automation.Language.Parser]::ParseInput(
            $unsafeVerifierText,
            [ref]$unsafeVerifierTokens,
            [ref]$unsafeVerifierErrors)
        Assert-True ($unsafeVerifierErrors.Count -eq 0) `
            "$($unsafeVerifierCall.Name) fixture did not parse."
        Assert-Throws -MessagePattern $unsafeVerifierCall.Message -Action {
            Assert-LhmCommonIdentityContract `
                -ScriptAst $unsafeVerifierAst `
            -Label "$($unsafeVerifierCall.Name) fixture"
        }
    }

    $unsafeCommonMetadata = @(
        @{
            Name = 'common expected machine'
            Search = '$script:LhmExpectedMachineId = ''snd-desk'''
            Replacement = '$script:LhmExpectedMachineId = ''snd-host'''
            Message = 'pinned to its reviewed literal'
        },
        @{
            Name = 'common expected installation'
            Search = '$script:LhmExpectedInstanceId = ''ca96d510-7d87-4cec-8e1a-bd8fc3866903'''
            Replacement = '$script:LhmExpectedInstanceId = ''11111111-1111-1111-1111-111111111111'''
            Message = 'pinned to its reviewed literal'
        },
        @{
            Name = 'common verifier path'
            Search = '''common_dev\v2\Test-LocalMachineIdentity.ps1'''
            Replacement = '''common_dev\v2\UntrustedIdentity.ps1'''
            Message = 'Windows LocalApplicationData known folder'
        }
    )
    foreach ($metadataCase in $unsafeCommonMetadata) {
        $unsafeMetadataText = $commonText.Replace(
            $metadataCase.Search,
            $metadataCase.Replacement)
        Assert-True ($unsafeMetadataText -cne $commonText) `
            "$($metadataCase.Name) fixture replacement did not apply."
        $unsafeMetadataTokens = $null
        $unsafeMetadataErrors = $null
        $unsafeMetadataAst = [System.Management.Automation.Language.Parser]::ParseInput(
            $unsafeMetadataText,
            [ref]$unsafeMetadataTokens,
            [ref]$unsafeMetadataErrors)
        Assert-True ($unsafeMetadataErrors.Count -eq 0) `
            "$($metadataCase.Name) fixture did not parse."
        Assert-Throws -MessagePattern $metadataCase.Message -Action {
            Assert-LhmCommonIdentityContract `
                -ScriptAst $unsafeMetadataAst `
                -Label "$($metadataCase.Name) fixture"
        }
    }

    $commonMachineAssignment = '$script:LhmExpectedMachineId = ''snd-desk'''
    $commonAliasText = $commonText.Replace(
        $commonMachineAssignment,
        $commonMachineAssignment + "`n" +
            '$script:LhmExpectedmachineid = ''snd-host''')
    Assert-True ($commonAliasText -cne $commonText) `
        'Common case-alias metadata fixture replacement did not apply.'
    $commonAliasTokens = $null
    $commonAliasErrors = $null
    $commonAliasAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $commonAliasText,
        [ref]$commonAliasTokens,
        [ref]$commonAliasErrors)
    Assert-True ($commonAliasErrors.Count -eq 0) `
        'Common case-alias metadata fixture did not parse.'
    Assert-Throws -MessagePattern 'assign .* exactly once' -Action {
        Assert-LhmCommonIdentityContract `
            -ScriptAst $commonAliasAst `
            -Label 'common case-alias metadata fixture'
    }

    $commonPlusText = $commonText.Replace(
        $commonMachineAssignment,
        '$script:LhmExpectedMachineId += ''snd-desk''')
    Assert-True ($commonPlusText -cne $commonText) `
        'Common additive metadata fixture replacement did not apply.'
    $commonPlusTokens = $null
    $commonPlusErrors = $null
    $commonPlusAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $commonPlusText,
        [ref]$commonPlusTokens,
        [ref]$commonPlusErrors)
    Assert-True ($commonPlusErrors.Count -eq 0) `
        'Common additive metadata fixture did not parse.'
    Assert-Throws -MessagePattern 'pinned to its reviewed literal' -Action {
        Assert-LhmCommonIdentityContract `
            -ScriptAst $commonPlusAst `
            -Label 'common additive metadata fixture'
    }

    $launcherText = [System.IO.File]::ReadAllText($canonicalLauncher)
    $unsafeLauncherMetadata = @(
        @{
            Name = 'launcher expected machine'
            Search = '$ExpectedMachineId = ''snd-desk'''
            Replacement = '$ExpectedMachineId = ''snd-host'''
            Message = 'pinned to its reviewed literal'
        },
        @{
            Name = 'launcher expected installation'
            Search = '$ExpectedInstanceId = ''ca96d510-7d87-4cec-8e1a-bd8fc3866903'''
            Replacement = '$ExpectedInstanceId = ''11111111-1111-1111-1111-111111111111'''
            Message = 'pinned to its reviewed literal'
        },
        @{
            Name = 'launcher verifier path'
            Search = '''common_dev\v2\Test-LocalMachineIdentity.ps1'''
            Replacement = '''common_dev\v2\UntrustedIdentity.ps1'''
            Message = 'Windows LocalApplicationData known folder'
        }
    )
    foreach ($metadataCase in $unsafeLauncherMetadata) {
        $unsafeMetadataText = $launcherText.Replace(
            $metadataCase.Search,
            $metadataCase.Replacement)
        Assert-True ($unsafeMetadataText -cne $launcherText) `
            "$($metadataCase.Name) fixture replacement did not apply."
        $unsafeMetadataTokens = $null
        $unsafeMetadataErrors = $null
        $unsafeMetadataAst = [System.Management.Automation.Language.Parser]::ParseInput(
            $unsafeMetadataText,
            [ref]$unsafeMetadataTokens,
            [ref]$unsafeMetadataErrors)
        Assert-True ($unsafeMetadataErrors.Count -eq 0) `
            "$($metadataCase.Name) fixture did not parse."
        Assert-Throws -MessagePattern $metadataCase.Message -Action {
            Assert-LhmLauncherIdentityGate `
                -ScriptAst $unsafeMetadataAst `
                -Label "$($metadataCase.Name) fixture"
        }
    }

    $launcherMachineAssignment = '$ExpectedMachineId = ''snd-desk'''
    $launcherAliasText = $launcherText.Replace(
        $launcherMachineAssignment,
        $launcherMachineAssignment + "`n" +
            '$Expectedmachineid = ''snd-host''')
    Assert-True ($launcherAliasText -cne $launcherText) `
        'Launcher case-alias metadata fixture replacement did not apply.'
    $launcherAliasTokens = $null
    $launcherAliasErrors = $null
    $launcherAliasAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $launcherAliasText,
        [ref]$launcherAliasTokens,
        [ref]$launcherAliasErrors)
    Assert-True ($launcherAliasErrors.Count -eq 0) `
        'Launcher case-alias metadata fixture did not parse.'
    Assert-Throws -MessagePattern 'assign .* exactly once' -Action {
        Assert-LhmLauncherIdentityGate `
            -ScriptAst $launcherAliasAst `
            -Label 'launcher case-alias metadata fixture'
    }

    $launcherPlusText = $launcherText.Replace(
        $launcherMachineAssignment,
        '$ExpectedMachineId += ''snd-desk''')
    Assert-True ($launcherPlusText -cne $launcherText) `
        'Launcher additive metadata fixture replacement did not apply.'
    $launcherPlusTokens = $null
    $launcherPlusErrors = $null
    $launcherPlusAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $launcherPlusText,
        [ref]$launcherPlusTokens,
        [ref]$launcherPlusErrors)
    Assert-True ($launcherPlusErrors.Count -eq 0) `
        'Launcher additive metadata fixture did not parse.'
    Assert-Throws -MessagePattern 'pinned to its reviewed literal' -Action {
        Assert-LhmLauncherIdentityGate `
            -ScriptAst $launcherPlusAst `
            -Label 'launcher additive metadata fixture'
    }

    return $true
}

$identityGateContractsVerified = Invoke-LhmIdentityContractPreflight
. $commonScript

if (-not $LauncherConvergenceOnly) {
    $managedTaskSettings = New-LhmManagedTaskSettings
    Assert-True ($managedTaskSettings.RestartCount -eq 3) `
        'Managed task restart count is not bounded to three attempts.'
    Assert-True ([string]$managedTaskSettings.RestartInterval -ceq 'PT1M') `
        'Managed task restart interval is not one minute.'
}

$relocationBehaviorAst = Get-LhmScriptAst -Path $relocationScript
foreach ($functionName in @(
    'Assert-LhmRelocationHealthUri',
    'Assert-LhmRelocationTaskContract'
)) {
    $functionAsts = @($relocationBehaviorAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq $functionName
    }, $true))
    Assert-True ($functionAsts.Count -eq 1) `
        "Relocation script must define $functionName exactly once."
    . ([scriptblock]::Create($functionAsts[0].Extent.Text))
}

function New-TestIdentityVerifier {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Name,
        [string] $Status,
        [string] $MachineId,
        [string] $InstanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903',
        [switch] $NoResult,
        [switch] $DuplicateResult
    )

    $path = Join-Path $Root "$Name.ps1"
    $content = if ($NoResult) {
        'return'
    }
    else {
$identityObject = @"
[pscustomobject][ordered]@{
    status = '$Status'
    machineId = '$MachineId'
    instanceId = '$InstanceId'
    computerName = 'SND-DESK'
    user = 'Sev'
    qualifiedUser = 'SND-DESK\Sev'
    sshAlias = 'maindesk'
    sshHostname = 'snd-desk.example.invalid'
    displayName = 'SND-DESK fixture'
    registryPath = 'C:\fixture\machines.json'
    markerPath = 'C:\fixture\machine-identity.json'
    verifiedAtUtc = '2030-01-01T00:00:00.0000000Z'
}
"@
        if ($DuplicateResult) {
            $identityObject + "`n" + $identityObject
        }
        else {
            $identityObject
        }
    }
    $content | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Invoke-TestLauncherIdentity {
    param(
        [Parameter(Mandatory)][string] $LauncherPath,
        [Parameter(Mandatory)][string] $VerifierPath
    )

    $windowsPowerShell =
        Get-Command powershell.exe -CommandType Application -ErrorAction Stop
    $launcherLiteral = $LauncherPath.Replace("'", "''")
    $verifierLiteral = $VerifierPath.Replace("'", "''")
    $harness = @"
function Get-Process {
    [CmdletBinding()]
    param([string] `$Name)
    return @()
}

. '$launcherLiteral' -ValidateScriptOnly | Out-Null
`$IdentityVerifierPath = '$verifierLiteral'
`$ExpectedMachineId = 'snd-desk'
`$ExpectedInstanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903'
Assert-LauncherMachineIdentity
"@
    $oldErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(
            & $windowsPowerShell.Source `
                -NoLogo `
                -NoProfile `
                -ExecutionPolicy Bypass `
                -Command $harness 2>&1
        )
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldErrorActionPreference
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = ($output -join "`n")
    }
}

function Get-TreeSignature {
    param([Parameter(Mandatory)][string] $Root)
    if (-not (Test-Path -LiteralPath $Root)) {
        return '<missing>'
    }

    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    return (@(Get-ChildItem -LiteralPath $rootPath -Force -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($rootPath.Length).TrimStart('\')
        if ($_.PSIsContainer) {
            "D|$relative"
        }
        else {
            "F|$relative|$($_.Length)|$((Get-LhmFileSha256 -Path $_.FullName))"
        }
    } | Sort-Object) -join "`n")
}

function New-TestCandidate {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Version,
        [Parameter(Mandatory)][string] $ShortCommit
    )

    $sourceRoot = Join-Path $Root "fixture-$Name"
    $outputRoot = Join-Path $sourceRoot 'out'
    $candidateRoot = Join-Path $Root "candidate-$Name"
    [System.IO.Directory]::CreateDirectory($sourceRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($candidateRoot) | Out-Null

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>LibreHardwareMonitor.Windows.Forms</AssemblyName>
    <Version>$Version</Version>
    <FileVersion>$Version.1</FileVersion>
    <InformationalVersion>$Version+$ShortCommit.20260725</InformationalVersion>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>false</SelfContained>
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $sourceRoot 'fixture.csproj') -Encoding UTF8
    "public static class Program { public static void Main() { System.Console.WriteLine(`"$Name`"); } }" |
        Set-Content -LiteralPath (Join-Path $sourceRoot 'Program.cs') -Encoding UTF8

    $publishOutput = @(& dotnet publish `
        (Join-Path $sourceRoot 'fixture.csproj') `
        -c Release `
        -o $outputRoot `
        --nologo)
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture publish failed for '$Name' with exit code $LASTEXITCODE.`n$($publishOutput -join "`n")"
    }

    $executablePath = Join-Path $candidateRoot $script:LhmExecutableName
    Copy-Item `
        -LiteralPath (Join-Path $outputRoot $script:LhmExecutableName) `
        -Destination $executablePath
    $productVersion =
        [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath).ProductVersion
    $commit = $ShortCommit + ('0' * (40 - $ShortCommit.Length))
    [ordered]@{
        schema = $script:LhmReleaseSchema
        releaseId = "$Name-$ShortCommit"
        commit = $commit
        version = $productVersion
        framework = 'net10.0-windows'
        runtime = 'win-x64'
        selfContained = $false
        timestamp = [DateTimeOffset]::UtcNow.ToString('o')
        sha256 = Get-LhmFileSha256 -Path $executablePath
    } | ConvertTo-Json | Set-Content `
        -LiteralPath (Join-Path $candidateRoot $script:LhmReleaseManifestName) `
        -Encoding UTF8

    $null = Read-LhmReleasePayload -Directory $candidateRoot -RequireCandidateShape
    return $candidateRoot
}

function New-ProductionRelocationTaskFixture {
    param(
        [Parameter(Mandatory)][string] $ExecutablePath,
        [Parameter(Mandatory)][string] $InstallRoot
    )

    return [pscustomobject]@{
        Actions = @([pscustomobject]@{
            Execute = $ExecutablePath
            Arguments = $null
            WorkingDirectory = $InstallRoot
        })
        Principal = [pscustomobject]@{
            UserId = $script:LhmManagedTaskPrincipalUserId
            LogonType = 'Interactive'
            RunLevel = 'Highest'
        }
        Settings = [pscustomobject]@{
            Enabled = $false
            MultipleInstances = 'IgnoreNew'
            StartWhenAvailable = $true
            AllowHardTerminate = $false
            RestartCount = 3
            RestartInterval = 'PT1M'
        }
        Triggers = @([pscustomobject]@{
            CimClass = [pscustomobject]@{ CimClassName = 'MSFT_TaskLogonTrigger' }
            UserId = $script:LhmManagedTaskLogonUserId
            Enabled = $true
        })
        State = 'Disabled'
    }
}

function New-LauncherConvergenceFixture {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Name,
        [switch] $BadIdentity,
        [switch] $SeedDestinations
    )

    $fixtureRoot = Join-Path $Root "launcher-convergence-$Name"
    $sourceRoot = Join-Path $fixtureRoot 'source'
    $runtimeScriptsRoot = Join-Path $fixtureRoot 'Monitoring\LibreHW\Scripts'
    $runtimeRoot = Join-Path $fixtureRoot 'Monitoring\LibreHW\Runtime'
    $dataRoot = Join-Path $fixtureRoot 'Data\LibreHardwareMonitor'
    $binRoot = Join-Path $fixtureRoot 'Bin'
    $authorityRoot = Join-Path $fixtureRoot 'RunW'
    foreach ($directory in @(
        $sourceRoot,
        $runtimeScriptsRoot,
        $runtimeRoot,
        $dataRoot,
        $binRoot,
        $authorityRoot
    )) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    $sourceLauncherPath = Join-Path $sourceRoot 'Start-LibreHardwareMonitor.ps1'
    $sourceShimPath = Join-Path $sourceRoot 'librehw.cmd'
    $centralLauncherPath = Join-Path $authorityRoot 'runw.exe'
    $centralLauncherAuthorityReceiptPath =
        Join-Path $authorityRoot 'install-receipt-v2.json'
    $runtimeLauncherPath = Join-Path $runtimeScriptsRoot 'Start-LibreHardwareMonitor.ps1'
    $legacyVendoredLauncherPath = Join-Path $runtimeScriptsRoot 'hidelaunch.exe'
    $runtimeExecutablePath =
        Join-Path $runtimeRoot 'LibreHardwareMonitor.Windows.Forms.exe'
    $publicShimPath = Join-Path $binRoot 'librehw.cmd'
    $receiptPath = Join-Path $dataRoot 'launcher-convergence\current.json'
    $rollbackRoot = Join-Path $dataRoot 'launcher-convergence\rollback'
    $taskStatePath = Join-Path $fixtureRoot 'task\managed-task.json'
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $taskStatePath)) |
        Out-Null

    '# launcher fixture' | Set-Content -LiteralPath $sourceLauncherPath -Encoding UTF8
    $shimText = "@echo off`r`n" +
        "`"$centralLauncherPath`" /wait /quiet /cwd:- " +
        "`"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`" " +
        "-NoLogo -NoProfile -ExecutionPolicy Bypass " +
        "-File `"$runtimeLauncherPath`" %*`r`n" +
        "exit /b %ERRORLEVEL%`r`n"
    [System.IO.File]::WriteAllText(
        $sourceShimPath,
        $shimText,
        [System.Text.Encoding]::ASCII)
    'validated central runw fixture' |
        Set-Content -LiteralPath $centralLauncherPath -Encoding ASCII
    'runtime executable fixture' |
        Set-Content -LiteralPath $runtimeExecutablePath -Encoding ASCII
    $centralLauncherHash = Get-LhmFileSha256 -Path $centralLauncherPath
    [ordered]@{
        Schema = 'runw.deployment.v2'
        Operation = 'Applied'
        AppliedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        ReceiptPath = $centralLauncherAuthorityReceiptPath
        Source = [ordered]@{
            Root = 'D:\Devtools\runW'
            Commit = '64472d33d43aeac0493781f1739c8ebe7ae1165b'
            Dirty = $false
            Inputs = @()
        }
        Artifact = [ordered]@{
            Path = 'D:\Devtools\runW\bin\runw.exe'
            Sha256 = $centralLauncherHash.ToUpperInvariant()
            Length = (Get-Item -LiteralPath $centralLauncherPath).Length
            Version = '1.3.1'
        }
        Destinations = @([ordered]@{
            Path = $centralLauncherPath
            Sha256 = $centralLauncherHash.ToUpperInvariant()
        })
        Aliases = @()
        Retired = @()
        RollbackDirectory = Join-Path $authorityRoot 'rollback'
        RollbackFiles = @()
    } | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath $centralLauncherAuthorityReceiptPath `
        -Encoding UTF8

    [ordered]@{
        taskPath = $script:LhmManagedTaskPath
        execute = $runtimeExecutablePath
        arguments = $null
        workingDirectory = $runtimeRoot
        principalUserId = $script:LhmManagedTaskPrincipalUserId
        logonType = 'Interactive'
        runLevel = 'Highest'
        multipleInstances = 'IgnoreNew'
        startWhenAvailable = $true
        allowHardTerminate = $false
        restartCount = 3
        restartInterval = 'PT1M'
        trigger = 'Logon'
        triggerUserId = $script:LhmManagedTaskLogonUserId
        enabled = $true
    } | ConvertTo-Json | Set-Content -LiteralPath $taskStatePath -Encoding UTF8

    $identityVerifierPath = New-TestIdentityVerifier `
        -Root $fixtureRoot `
        -Name 'identity' `
        -Status $(if ($BadIdentity) { 'UNVERIFIED' } else { 'VERIFIED' }) `
        -MachineId 'snd-desk'

    if ($SeedDestinations) {
        'old launcher' | Set-Content -LiteralPath $runtimeLauncherPath -Encoding UTF8
        '@echo off' | Set-Content -LiteralPath $publicShimPath -Encoding ASCII
    }

    return [pscustomobject]@{
        Root = $fixtureRoot
        SourceLauncherPath = $sourceLauncherPath
        SourceShimPath = $sourceShimPath
        CentralLauncherPath = $centralLauncherPath
        CentralLauncherAuthorityReceiptPath =
            $centralLauncherAuthorityReceiptPath
        RuntimeLauncherPath = $runtimeLauncherPath
        LegacyVendoredLauncherPath = $legacyVendoredLauncherPath
        RuntimeExecutablePath = $runtimeExecutablePath
        PublicShimPath = $publicShimPath
        ReceiptPath = $receiptPath
        RollbackRoot = $rollbackRoot
        TaskStatePath = $taskStatePath
        IdentityVerifierPath = $identityVerifierPath
    }
}

function New-DataRootRelocationFixture {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $CandidateDirectory
    )

    $fixtureRoot = Join-Path $Root "relocation-$Name"
    $installRoot = Join-Path $fixtureRoot 'install'
    $sourceDataRoot = Join-Path $fixtureRoot 'old-data'
    $dataRoot = Join-Path $fixtureRoot 'new-data'
    $externalRoot = Join-Path $fixtureRoot 'external-state'
    $launcherTarget = Join-Path $fixtureRoot 'script-data\Start-LibreHardwareMonitor.ps1'
    $shimPath = Join-Path $fixtureRoot 'bin\librehw.cmd'
    $recoveryParent = Join-Path $dataRoot 'release-recovery'
    $preStableRecoveryRoot = Join-Path $recoveryParent 'pre-stable-startup'

    [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($dataRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $dataRoot 'logs')) | Out-Null
    [System.IO.Directory]::CreateDirectory($externalRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $launcherTarget)) | Out-Null
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $shimPath)) | Out-Null
    [System.IO.Directory]::CreateDirectory($preStableRecoveryRoot) | Out-Null

    $null = Copy-LhmPayloadPair `
        -SourceDirectory $CandidateDirectory `
        -DestinationDirectory $installRoot
    [ordered]@{
        schema = $script:LhmRuntimeSchema
        dataRoot = $sourceDataRoot
        managedStartupTaskPath = $script:LhmManagedTaskPath
    } | ConvertTo-Json | Set-Content `
        -LiteralPath (Join-Path $installRoot $script:LhmRuntimeConfigName) `
        -Encoding UTF8
    '<configuration><appSettings /></configuration>' | Set-Content `
        -LiteralPath (Join-Path $dataRoot $script:LhmSettingsFileName) `
        -Encoding UTF8
    'relocation log sentinel' | Set-Content `
        -LiteralPath (Join-Path $dataRoot 'logs\sentinel.csv') `
        -Encoding UTF8
    @"
# Pre-relocation launcher fixture.
`$DataRoot = '$sourceDataRoot'
"@ | Set-Content -LiteralPath $launcherTarget -Encoding UTF8
    @"
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$launcherTarget" %*
"@ | Set-Content -LiteralPath $shimPath -Encoding ASCII
    [ordered]@{
        taskPath = $script:LhmManagedTaskPath
        execute = Join-Path $installRoot $script:LhmExecutableName
        arguments = $null
        workingDirectory = $installRoot
        principalUserId = $script:LhmManagedTaskPrincipalUserId
        runLevel = 'Highest'
        logonType = 'InteractiveToken'
        trigger = 'LogonAndOnDemand'
        triggerUserId = $script:LhmManagedTaskLogonUserId
        multipleInstances = 'IgnoreNew'
        startWhenAvailable = $true
        allowHardTerminate = $false
        restartCount = 3
        restartInterval = 'PT1M'
        enabled = $false
    } | ConvertTo-Json | Set-Content `
        -LiteralPath (Join-Path $externalRoot 'managed-task.json') `
        -Encoding UTF8

    $shimHash = Get-LhmFileSha256 -Path $shimPath
    [ordered]@{
        schema = 'sq.librehw.pre-stable-startup-recovery.v1'
        createdAt = [DateTimeOffset]::UtcNow.ToString('o')
        launcherTargetPath = $launcherTarget
        launcherExisted = $false
        launcherBackup = $null
        launcherSha256 = $null
        managedTaskPath = $script:LhmManagedTaskPath
        managedTaskExisted = $false
        managedTaskBackup = $null
        managedTaskSha256 = $null
        publicShimPath = $shimPath
        publicShimSha256 = $shimHash
    } | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath (Join-Path $preStableRecoveryRoot 'recovery.json') `
        -Encoding UTF8

    return [pscustomobject]@{
        Root = $fixtureRoot
        InstallRoot = $installRoot
        SourceDataRoot = $sourceDataRoot
        DataRoot = $dataRoot
        ExternalRoot = $externalRoot
        LauncherTarget = $launcherTarget
        ShimPath = $shimPath
        PreStableRecoveryRoot = $preStableRecoveryRoot
        RelocationRecoveryRoot = Join-Path $recoveryParent 'data-root-relocation'
    }
}

function New-RuntimeRootMigrationFixture {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $CandidateDirectory
    )

    $fixtureRoot = Join-Path $Root "runtime-migration-$Name"
    $legacyInstallRoot = Join-Path $fixtureRoot 'legacy\LibreHW'
    $installRoot = Join-Path $fixtureRoot 'Monitoring\LibreHW\Runtime'
    $dataRoot = Join-Path $fixtureRoot 'Data\LibreHardwareMonitor'
    $legacyLauncherPath = Join-Path `
        $fixtureRoot `
        'UserProfile\script-data\Start-LibreHardwareMonitor.ps1'
    $launcherTargetPath = Join-Path `
        $fixtureRoot `
        'Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1'
    $runtimeHideLaunchPath =
        Join-Path (Split-Path -Parent $launcherTargetPath) 'hidelaunch.exe'
    $hideLaunchArtifactPath =
        Join-Path $fixtureRoot 'HideLaunch\source\hidelaunch.exe'
    $hideLaunchAuthorityReceiptPath =
        Join-Path $fixtureRoot 'HideLaunch\install-receipt.json'
    $publicShimPath = Join-Path $fixtureRoot 'Bin\librehw.cmd'
    $taskStatePath = Join-Path $fixtureRoot 'task\managed-task.json'

    foreach ($directory in @(
        $legacyInstallRoot,
        $dataRoot,
        (Split-Path -Parent $legacyLauncherPath),
        (Split-Path -Parent $launcherTargetPath),
        (Split-Path -Parent $hideLaunchArtifactPath),
        (Split-Path -Parent $publicShimPath),
        (Split-Path -Parent $taskStatePath)
    )) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $CandidateDirectory `
        -DestinationDirectory $legacyInstallRoot `
        -DestinationMayContainOtherEntries
    [System.IO.Directory]::CreateDirectory(
        (Join-Path $legacyInstallRoot 'rollback')) | Out-Null
    [ordered]@{
        schema = $script:LhmRuntimeSchema
        dataRoot = $dataRoot
        managedStartupTaskPath = $script:LhmManagedTaskPath
    } | ConvertTo-Json | Set-Content `
        -LiteralPath (Join-Path $legacyInstallRoot $script:LhmRuntimeConfigName) `
        -Encoding UTF8
    '# legacy launcher' | Set-Content `
        -LiteralPath $legacyLauncherPath `
        -Encoding UTF8
    "@echo off`r`nrem legacy shim`r`n" | Set-Content `
        -LiteralPath $publicShimPath `
        -Encoding Ascii `
        -NoNewline
    'validated migration hidelaunch fixture' | Set-Content `
        -LiteralPath $hideLaunchArtifactPath `
        -Encoding ASCII
    Copy-Item `
        -LiteralPath $hideLaunchArtifactPath `
        -Destination $runtimeHideLaunchPath
    $hideLaunchHash = Get-LhmFileSha256 -Path $hideLaunchArtifactPath
    [ordered]@{
        Schema = 'runw.deployment.v2'
        Source = [ordered]@{
            Root = Split-Path -Parent $hideLaunchArtifactPath
            Commit = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
        }
        Artifact = [ordered]@{
            Path = $hideLaunchArtifactPath
            Sha256 = $hideLaunchHash.ToUpperInvariant()
            Length = (Get-Item -LiteralPath $hideLaunchArtifactPath).Length
            Version = '1.1.0'
        }
    } | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath $hideLaunchAuthorityReceiptPath `
        -Encoding UTF8
    [ordered]@{
        execute = Join-Path $legacyInstallRoot $script:LhmExecutableName
        workingDirectory = $legacyInstallRoot
        restartCount = 3
        restartInterval = 'PT1M'
        enabled = $true
    } | ConvertTo-Json | Set-Content `
        -LiteralPath $taskStatePath `
        -Encoding UTF8

    return [pscustomobject]@{
        Root = $fixtureRoot
        LegacyInstallRoot = $legacyInstallRoot
        InstallRoot = $installRoot
        DataRoot = $dataRoot
        LegacyLauncherPath = $legacyLauncherPath
        LauncherTargetPath = $launcherTargetPath
        RuntimeHideLaunchPath = $runtimeHideLaunchPath
        HideLaunchArtifactPath = $hideLaunchArtifactPath
        HideLaunchAuthorityReceiptPath = $hideLaunchAuthorityReceiptPath
        PublicShimPath = $publicShimPath
        TaskStatePath = $taskStatePath
        RecoveryRoot = Join-Path `
            $dataRoot `
            'release-recovery\runtime-root-relocation'
    }
}

function Assert-NoTransactionDebris {
    param([Parameter(Mandatory)][string] $InstallRoot)
    $debris = @(Get-ChildItem -LiteralPath $InstallRoot -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '.release-*' })
    $debrisNames = @($debris | ForEach-Object Name)
    Assert-True ($debris.Count -eq 0) "Transaction debris remains: $($debrisNames -join ', ')."
}

function New-LegacyRecoveryFixture {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $PublicShimPath,
        [Parameter(Mandatory)][string] $PublicShimSha256
    )

    [System.IO.Directory]::CreateDirectory($Root) | Out-Null
    $legacyExecutable = $script:LhmLegacyRootTaskExecutablePath
    $legacyWorkingDirectory = Split-Path -Parent $legacyExecutable
    $userSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><URI>\LibreHardwareMonitor</URI></RegistrationInfo>
  <Principals><Principal id="Author"><UserId>$userSid</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings><AllowHardTerminate>false</AllowHardTerminate><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><StartWhenAvailable>true</StartWhenAvailable></Settings>
  <Triggers><LogonTrigger /></Triggers>
  <Actions Context="Author"><Exec><Command>$legacyExecutable</Command><WorkingDirectory>$legacyWorkingDirectory</WorkingDirectory></Exec></Actions>
</Task>
"@
    $taskXmlPath = Join-Path $Root 'legacy-task.xml'
    $taskXml | Set-Content -LiteralPath $taskXmlPath -Encoding Unicode
    $shell = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        for ($index = 0; $index -lt 2; $index++) {
            $shortcut = $null
            try {
                $shortcut = $shell.CreateShortcut((Join-Path $Root "$index.lnk"))
                $shortcut.TargetPath = $script:LhmLegacyShortcutTargetPath[$index]
                $shortcut.Arguments = $script:LhmLegacyShortcutArguments[$index]
                $shortcut.WorkingDirectory = $script:LhmLegacyShortcutWorkingDirectory
                $shortcut.Save()
            }
            finally {
                if ($null -ne $shortcut -and
                    [System.Runtime.InteropServices.Marshal]::IsComObject($shortcut)) {
                    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
                }
            }
        }
    }
    finally {
        if ($null -ne $shell -and
            [System.Runtime.InteropServices.Marshal]::IsComObject($shell)) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
    }

    [ordered]@{
        schema = 'sq.librehw.legacy-cutover-recovery.v1'
        createdAt = [DateTimeOffset]::UtcNow.ToString('o')
        legacyTaskPath = '\LibreHardwareMonitor'
        legacyTaskXml = 'legacy-task.xml'
        legacyTaskXmlSha256 = Get-LhmFileSha256 -Path $taskXmlPath
        publicShimPath = $PublicShimPath
        publicShimSha256 = $PublicShimSha256
        shortcuts = @(
            [ordered]@{
                path = 'C:\Users\Sev\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\LibreHardwareMonitor.Windows.Forms.lnk'
                existed = $true
                backup = '0.lnk'
                sha256 = Get-LhmFileSha256 -Path (Join-Path $Root '0.lnk')
            },
            [ordered]@{
                path = 'C:\Users\Sev\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\LibreHW-No-UAC.lnk'
                existed = $true
                backup = '1.lnk'
                sha256 = Get-LhmFileSha256 -Path (Join-Path $Root '1.lnk')
            }
        )
    } | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $Root 'recovery.json') `
        -Encoding UTF8

    return $Root
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "sq-librehw-release-test-$([guid]::NewGuid().ToString('N'))"
$installRoot = Join-Path $testRoot 'install'
$dataRoot = Join-Path $testRoot 'data'
$externalRoot = Join-Path $testRoot 'external-state'
$launcherTarget = Join-Path $testRoot 'script-data\Start-LibreHardwareMonitor.ps1'
$shimPath = Join-Path $testRoot 'bin\librehw.cmd'
$initialConfig = Join-Path $testRoot 'initial.config'
$oldNugetPackages = $env:NUGET_PACKAGES
$oldDotnetNoLogo = $env:DOTNET_NOLOGO
$oldDotnetTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$oldDotnetCertificate = $env:DOTNET_GENERATE_ASPNET_CERTIFICATE

try {
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $launcherTarget)) | Out-Null
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $shimPath)) | Out-Null
    [System.IO.Directory]::CreateDirectory($externalRoot) | Out-Null
    '@echo off' | Set-Content -LiteralPath $shimPath -Encoding ASCII
    'legacy launcher' | Set-Content -LiteralPath $launcherTarget -Encoding UTF8
    '{"legacy":true}' | Set-Content `
        -LiteralPath (Join-Path $externalRoot 'managed-task.json') `
        -Encoding UTF8
    '<configuration><appSettings /></configuration>' |
        Set-Content -LiteralPath $initialConfig -Encoding UTF8
    $shimHash = Get-LhmFileSha256 -Path $shimPath
    $legacyLauncherHash = Get-LhmFileSha256 -Path $launcherTarget
    $legacyTaskHash = Get-LhmFileSha256 -Path (Join-Path $externalRoot 'managed-task.json')

    $env:NUGET_PACKAGES = Join-Path $testRoot 'nuget'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    if (-not $LauncherConvergenceOnly) {
        $candidate1 = New-TestCandidate `
            -Root $testRoot -Name 'one' -Version '1.0.1' -ShortCommit 'abcde01'
        $candidate2 = New-TestCandidate `
            -Root $testRoot -Name 'two' -Version '1.0.2' -ShortCommit 'abcde02'
    }

    Assert-True (Test-Path -LiteralPath $canonicalPublicShim -PathType Leaf) `
        'Canonical librehw.cmd source is missing.'
    Assert-True (Test-Path -LiteralPath $launcherConvergenceScript -PathType Leaf) `
        'Launcher convergence script is missing.'
    $canonicalShimText = [System.IO.File]::ReadAllText($canonicalPublicShim)
    Assert-True ($canonicalShimText -notmatch '(?im)^\s*start(?:\s|$)') `
        'Canonical librehw.cmd must not dispatch asynchronously with start.'
    Assert-True (
        $canonicalShimText -match [regex]::Escape(
            '%SEV_LOCAL_BIN%\runw\runw.exe') -and
        $canonicalShimText -notmatch '(?i)E:\\SevLocal\\' -and
        $canonicalShimText -notmatch '(?i)E:\\Bin\\' -and
        $canonicalShimText -match '(?i)/wait\s+/quiet\s+/cwd:-' -and
        $canonicalShimText -match [regex]::Escape(
            '"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"') -and
        $canonicalShimText -notmatch '(?i)/cwd:-\s+powershell\.exe\b' -and
        $canonicalShimText -match [regex]::Escape(
            '-File "%MACHINE_TOOLS_ROOT%\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1"') -and
        $canonicalShimText -match [regex]::Escape('%*') -and
        $canonicalShimText -match [regex]::Escape('exit /b %ERRORLEVEL%') -and
        $canonicalShimText -notmatch '(?i)librehw\.cmd' -and
        $canonicalShimText -notmatch [regex]::Escape(
            'E:\Monitoring\LibreHW\Scripts\hidelaunch.exe') -and
        $canonicalShimText -notmatch '(?i)(?:^|\s)/focus(?:\s|$)'
    ) 'Canonical librehw.cmd is not a central, wait-capable, non-recursive app launcher.'
    $normalizedCanonicalShimText =
        ($canonicalShimText -replace "`r?`n", "`r`n")
    $normalizedCanonicalShimPath = Join-Path $testRoot 'normalized-librehw.cmd'
    [System.IO.File]::WriteAllText(
        $normalizedCanonicalShimPath,
        $normalizedCanonicalShimText,
        [System.Text.Encoding]::ASCII)
    Assert-True (
        (Get-LhmFileSha256 -Path $normalizedCanonicalShimPath) -ceq
            $script:LhmPublicShimSha256
    ) 'Canonical public-shim deployment bytes do not match the shared release pin.'

    $runtimeMigrationAst = Get-LhmScriptAst -Path $runtimeMigrationScript
    $runtimeShimFunctions = @($runtimeMigrationAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq 'Get-LhmRuntimeMigrationShimText'
    }, $true))
    Assert-True ($runtimeShimFunctions.Count -eq 1) `
        'Runtime migration must define its public-shim generator exactly once.'
    . ([scriptblock]::Create($runtimeShimFunctions[0].Extent.Text))
    $runtimeMigrationShimText = Get-LhmRuntimeMigrationShimText `
        -LauncherPath 'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1'
    Assert-True (
        $runtimeMigrationShimText -match '(?i)hidelaunch\.exe' -and
        $runtimeMigrationShimText -notmatch [regex]::Escape(
            '%SEV_LOCAL_BIN%\runw\runw.exe') -and
        $runtimeMigrationShimText -notmatch '(?i)E:\\SevLocal\\'
    ) 'Historical runtime migration no longer exposes its app-vendored migration contract.'

    $launcherConvergenceText =
        [System.IO.File]::ReadAllText($launcherConvergenceScript)
    $commonTextForReceipt = [System.IO.File]::ReadAllText($commonScript)
    Assert-True (
        $commonTextForReceipt -match
            [regex]::Escape('RunW\install-receipt-v2-1.3.1-64472d3-help-docs.json') -and
        $commonTextForReceipt -notmatch
            [regex]::Escape('RunW\install-receipt-v2-1.3.1-b5cda6d-relocated.json') -and
        $commonTextForReceipt -notmatch
            [regex]::Escape("'RunW\install-receipt-v2-1.3.1-b5cda6d.json'") -and
        $launcherConvergenceText -match
            [regex]::Escape('Get-LhmProductionRunWReceiptPath') -and
        $launcherConvergenceText -match
            [regex]::Escape("'Applied', 'Relocated'") -and
        $launcherConvergenceText -notmatch '(?i)E:\\SevLocal\\' -and
        $commonTextForReceipt -notmatch
            [regex]::Escape('E:\Data\RunW\install-receipt-v2-1.3.1-b5cda6d.json') -and
        $launcherConvergenceText -notmatch
            [regex]::Escape('E:\Data\RunW\install-receipt-v2-1.3.1-b5cda6d.json') -and
        $launcherConvergenceText -notmatch
            [regex]::Escape('E:\Data\RunW\install-receipt-v2.json')
    ) 'Launcher convergence production receipt pins are not the current immutable RunW receipt.'
    foreach ($productionRunWProvenancePin in @(
        'D:\Devtools\runW',
        'D:\Devtools\runW\bin\runw.exe',
        '64472d33d43aeac0493781f1739c8ebe7ae1165b',
        '422F4534350A8174712345C972C5618F320A7FDDFFED5BD46368A91EBCDA9E96',
        '316928',
        '1.3.1'
    )) {
        Assert-True (
            $launcherConvergenceText.Contains($productionRunWProvenancePin)
        ) "Launcher convergence is missing RunW provenance pin '$productionRunWProvenancePin'."
    }
    foreach ($forbiddenTaskCommand in @(
        'Register-ScheduledTask',
        'Set-ScheduledTask',
        'Unregister-ScheduledTask',
        'Start-ScheduledTask'
    )) {
        Assert-True (
            $launcherConvergenceText -notmatch
                ('(?i)\b' + [regex]::Escape($forbiddenTaskCommand) + '\b')
        ) "Launcher convergence must not call $forbiddenTaskCommand."
    }
    $launcherConvergenceAst = Get-LhmScriptAst -Path $launcherConvergenceScript
    $convergenceShimFunctions = @($launcherConvergenceAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq 'Get-LhmLauncherExpectedShimText'
    }, $true))
    Assert-True ($convergenceShimFunctions.Count -eq 1) `
        'Launcher convergence must define its public-shim generator exactly once.'
    . ([scriptblock]::Create($convergenceShimFunctions[0].Extent.Text))
    $productionExpectedShimText = & {
        $NonLiveTestMode = $false
        function Get-LhmRawPersistedEnvironmentValue {
            param($Name)
            if ($Name -ceq 'MACHINE_TOOLS_ROOT') { return 'E:\' }
        }
        Get-LhmLauncherExpectedShimText `
            -CentralLauncherPath 'E:\SevLocal\Bin\runw\runw.exe' `
            -LauncherPath 'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1'
        Assert-Throws {
            Get-LhmLauncherExpectedShimText `
                -CentralLauncherPath 'E:\SevLocal\Bin\runw\runw.exe' `
                -LauncherPath 'C:\foreign\Start-LibreHardwareMonitor.ps1'
        } 'Public shim launcher token does not resolve to the scoped runtime launcher'
        function Get-LhmRawPersistedEnvironmentValue {
            param($Name)
            if ($Name -ceq 'MACHINE_TOOLS_ROOT') { return '..\..' }
        }
        Assert-Throws {
            Get-LhmLauncherExpectedShimText `
                -CentralLauncherPath 'E:\SevLocal\Bin\runw\runw.exe' `
                -LauncherPath 'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1'
        } 'Public shim launcher token must resolve to an absolute path'
        function Get-LhmRawPersistedEnvironmentValue {
            param($Name)
            return $null
        }
        Assert-Throws {
            Get-LhmLauncherExpectedShimText `
                -CentralLauncherPath 'E:\SevLocal\Bin\runw\runw.exe' `
                -LauncherPath 'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1'
        } 'which is not set at User, Machine, or Process scope'
    }
    Assert-True ($productionExpectedShimText -ceq $normalizedCanonicalShimText) `
        'Production convergence must preserve the portable canonical public-shim tokens.'

    $finalizerAst = Get-LhmScriptAst -Path $finalizeScript
    $finalizerBindingFunctions = @($finalizerAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq 'Assert-LhmFinalizerLauncherBinding'
    }, $true))
    Assert-True ($finalizerBindingFunctions.Count -eq 1) `
        'Finalizer must define its public-shim binding check exactly once.'
    $finalizerBindingCalls = @($finalizerAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.GetCommandName() -ceq 'Assert-LhmFinalizerLauncherBinding'
    }, $true))
    Assert-True ($finalizerBindingCalls.Count -eq 1) `
        'Finalizer must invoke its public-shim binding check exactly once.'
    . ([scriptblock]::Create($finalizerBindingFunctions[0].Extent.Text))
    & {
        function Get-LhmRawPersistedEnvironmentValue {
            param($Name)
            if ($Name -ceq 'MACHINE_TOOLS_ROOT') { return 'E:\' }
        }
        Assert-LhmFinalizerLauncherBinding `
            -ShimText $normalizedCanonicalShimText `
            -LauncherTargetPath 'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1'
        function Get-LhmRawPersistedEnvironmentValue {
            param($Name)
            if ($Name -ceq 'MACHINE_TOOLS_ROOT') { return 'C:\' }
        }
        Assert-Throws {
            Assert-LhmFinalizerLauncherBinding `
                -ShimText $normalizedCanonicalShimText `
                -LauncherTargetPath 'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1'
        } 'Public shim launcher token does not resolve to the scoped runtime launcher'
        function Get-LhmRawPersistedEnvironmentValue {
            param($Name)
            if ($Name -ceq 'MACHINE_TOOLS_ROOT') { return '..\..' }
        }
        Assert-Throws {
            Assert-LhmFinalizerLauncherBinding `
                -ShimText $normalizedCanonicalShimText `
                -LauncherTargetPath 'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1'
        } 'Public shim launcher token must resolve to an absolute path'
        function Get-LhmRawPersistedEnvironmentValue {
            param($Name)
            return $null
        }
        Assert-Throws {
            Assert-LhmFinalizerLauncherBinding `
                -ShimText $normalizedCanonicalShimText `
                -LauncherTargetPath 'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1'
        } 'which is not set at User, Machine, or Process scope'
    }

    $shimSemanticsRoot = Join-Path $testRoot 'launcher-shim-semantics'
    [System.IO.Directory]::CreateDirectory($shimSemanticsRoot) | Out-Null
    $semanticRunW = Join-Path $shimSemanticsRoot 'runw.cmd'
    $semanticLauncher = Join-Path $shimSemanticsRoot 'Start-LibreHardwareMonitor.ps1'
    $semanticShim = Join-Path $shimSemanticsRoot 'librehw.cmd'
    $semanticArguments = Join-Path $shimSemanticsRoot 'arguments.txt'
    '# launcher semantic fixture' |
        Set-Content -LiteralPath $semanticLauncher -Encoding UTF8
    $semanticRunWText = "@echo off`r`n" +
        "> `"$semanticArguments`" echo %*`r`n" +
        "exit /b 23`r`n"
    [System.IO.File]::WriteAllText(
        $semanticRunW,
        $semanticRunWText,
        [System.Text.Encoding]::ASCII)
    $semanticShimText = $canonicalShimText.Replace(
        '%SEV_LOCAL_BIN%\runw\runw.exe',
        $semanticRunW).Replace(
        '%MACHINE_TOOLS_ROOT%\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1',
        $semanticLauncher)
    [System.IO.File]::WriteAllText(
        $semanticShim,
        $semanticShimText,
        [System.Text.Encoding]::ASCII)
    & $semanticShim 'first-argument' 'second argument'
    $semanticExitCode = $LASTEXITCODE
    $semanticArgumentText =
        [System.IO.File]::ReadAllText($semanticArguments).Trim()
    Assert-True ($semanticExitCode -eq 23) `
        'Canonical librehw.cmd did not propagate the hidden helper exit status.'
    Assert-True (
        $semanticArgumentText -match '(?i)^/wait\s+/quiet\s+/cwd:-\s+' -and
        $semanticArgumentText -match [regex]::Escape('-File "' + $semanticLauncher + '"') -and
        $semanticArgumentText -match [regex]::Escape('first-argument') -and
        $semanticArgumentText -match [regex]::Escape('second argument')
    ) 'Canonical librehw.cmd did not forward launcher arguments through central RunW.'

    $launcherConvergence = New-LauncherConvergenceFixture `
        -Root $testRoot `
        -Name 'success'
    $launcherConvergenceParameters = @{
        CanonicalLauncherPath = $launcherConvergence.SourceLauncherPath
        CanonicalShimPath = $launcherConvergence.SourceShimPath
        RuntimeLauncherPath = $launcherConvergence.RuntimeLauncherPath
        RuntimeExecutablePath = $launcherConvergence.RuntimeExecutablePath
        PublicShimPath = $launcherConvergence.PublicShimPath
        CentralLauncherPath = $launcherConvergence.CentralLauncherPath
        CentralLauncherAuthorityReceiptPath =
            $launcherConvergence.CentralLauncherAuthorityReceiptPath
        ReceiptPath = $launcherConvergence.ReceiptPath
        RollbackRoot = $launcherConvergence.RollbackRoot
        TestManagedTaskStatePath = $launcherConvergence.TaskStatePath
        TestIdentityVerifierPath = $launcherConvergence.IdentityVerifierPath
        RollbackRetentionCount = 2
        NonLiveTestMode = $true
    }
    $launcherTaskHash = Get-LhmFileSha256 -Path $launcherConvergence.TaskStatePath
    $launcherPlan = & $launcherConvergenceScript `
        -Mode Plan `
        @launcherConvergenceParameters
    Assert-True (
        $launcherPlan.Result -ceq 'DRIFT' -and
        [bool]$launcherPlan.DriftDetected -and
        [string]$launcherPlan.ProposedReceiptSchema -ceq
            'sq.librehw.launcher-convergence.v2' -and
        -not [bool]$launcherPlan.MutationPerformed -and
        @($launcherPlan.Issues).Count -gt 0
    ) 'Launcher convergence Plan did not report the missing fixture deployment.'
    $launcherValidateDrift = & $launcherConvergenceScript `
        -Mode Validate `
        @launcherConvergenceParameters
    Assert-True (
        $launcherValidateDrift.Result -ceq 'DRIFT' -and
        [bool]$launcherValidateDrift.DriftDetected -and
        -not [bool]$launcherValidateDrift.MutationPerformed
    ) 'Launcher convergence Validate did not report drift without mutation.'

    $centralLauncherHash =
        Get-LhmFileSha256 -Path $launcherConvergence.CentralLauncherPath
    $launcherApply = & $launcherConvergenceScript `
        -Mode Apply `
        @launcherConvergenceParameters `
        -Confirm:$false
    Assert-True (
        $launcherApply.Result -ceq 'PASS' -and
        -not [bool]$launcherApply.DriftDetected -and
        [bool]$launcherApply.MutationPerformed -and
        (Get-LhmFileSha256 -Path $launcherConvergence.RuntimeLauncherPath) -ceq
            (Get-LhmFileSha256 -Path $launcherConvergence.SourceLauncherPath) -and
        (Get-LhmFileSha256 -Path $launcherConvergence.CentralLauncherPath) -ceq
            $centralLauncherHash -and
        -not (Test-Path `
            -LiteralPath $launcherConvergence.LegacyVendoredLauncherPath `
            -PathType Leaf) -and
        (Get-LhmFileSha256 -Path $launcherConvergence.PublicShimPath) -ceq
            (Get-LhmFileSha256 -Path $launcherConvergence.SourceShimPath) -and
        (Get-LhmFileSha256 -Path $launcherConvergence.TaskStatePath) -ceq
            $launcherTaskHash -and
        (Test-Path -LiteralPath $launcherConvergence.ReceiptPath -PathType Leaf)
    ) 'Launcher convergence Apply did not deploy the exact fixture artifacts.'
    $launcherReceipt = Get-Content `
        -LiteralPath $launcherConvergence.ReceiptPath `
        -Raw | ConvertFrom-Json
    Assert-True (
        [string]$launcherReceipt.schema -ceq
            'sq.librehw.launcher-convergence.v2' -and
        @($launcherReceipt.targets).Count -eq 2 -and
        [string]$launcherReceipt.centralLauncher.path -ceq
            $launcherConvergence.CentralLauncherPath -and
        [string]$launcherReceipt.centralLauncher.sha256 -ceq
            $centralLauncherHash -and
        [string]$launcherReceipt.centralLauncher.authorityReceiptPath -ceq
            $launcherConvergence.CentralLauncherAuthorityReceiptPath -and
        @($launcherReceipt.targets | Where-Object {
            [string]$_.path -ceq $launcherConvergence.CentralLauncherPath
        }).Count -eq 0
    ) 'Launcher convergence receipt is missing its typed central-launcher v2 record.'
    $launcherRollbackManifest = Get-Content `
        -LiteralPath ([string]$launcherReceipt.rollbackManifestPath) `
        -Raw | ConvertFrom-Json
    Assert-True (
        @($launcherRollbackManifest.files).Count -eq 2 -and
        @($launcherRollbackManifest.files | Where-Object {
            [string]$_.originalPath -ceq $launcherConvergence.CentralLauncherPath
        }).Count -eq 0
    ) 'Launcher convergence incorrectly owns central RunW rollback.'
    $launcherValidate = & $launcherConvergenceScript `
        -Mode Validate `
        @launcherConvergenceParameters
    Assert-True (
        $launcherValidate.Result -ceq 'PASS' -and
        -not [bool]$launcherValidate.DriftDetected -and
        -not [bool]$launcherValidate.MutationPerformed
    ) 'Launcher convergence Validate did not accept the converged fixture.'

    $legacyReceipt = [ordered]@{
        schema = 'sq.librehw.launcher-convergence.v1'
        appliedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        source = $launcherReceipt.source
        hideLaunch = [ordered]@{
            artifactPath = Join-Path `
                $launcherConvergence.Root `
                'legacy-build-output\runw.exe'
            artifactSha256 = $centralLauncherHash
            authorityReceiptPath =
                $launcherConvergence.CentralLauncherAuthorityReceiptPath
            authorityReceiptSha256 = Get-LhmFileSha256 `
                -Path $launcherConvergence.CentralLauncherAuthorityReceiptPath
            runtimePath = $launcherConvergence.LegacyVendoredLauncherPath
        }
        managedTask = $launcherReceipt.managedTask
        targets = @(
            $launcherReceipt.targets
            [ordered]@{
                role = 'hidelaunch'
                path = $launcherConvergence.LegacyVendoredLauncherPath
                sha256 = $centralLauncherHash
            }
        )
        rollbackDirectory = $launcherReceipt.rollbackDirectory
        rollbackManifestPath = $launcherReceipt.rollbackManifestPath
        rollbackManifestSha256 = $launcherReceipt.rollbackManifestSha256
    }
    $legacyReceipt | ConvertTo-Json -Depth 8 | Set-Content `
        -LiteralPath $launcherConvergence.ReceiptPath `
        -Encoding UTF8
    $legacyReceiptPlan = & $launcherConvergenceScript `
        -Mode Plan `
        @launcherConvergenceParameters
    Assert-True (
        $legacyReceiptPlan.Result -ceq 'DRIFT' -and
        -not [bool]$legacyReceiptPlan.ReceiptCurrent -and
        [bool]$legacyReceiptPlan.ReceiptMigrationRequired -and
        [string]$legacyReceiptPlan.ProposedReceiptSchema -ceq
            'sq.librehw.launcher-convergence.v2' -and
        @($legacyReceiptPlan.BlockingIssues).Count -eq 0 -and
        @($legacyReceiptPlan.Issues) -match 'migrat' -and
        (Get-LhmFileSha256 -Path $launcherConvergence.CentralLauncherPath) -ceq
            $centralLauncherHash -and
        -not (Test-Path `
            -LiteralPath $launcherConvergence.LegacyVendoredLauncherPath `
            -PathType Leaf)
    ) 'Launcher convergence did not expose v1 as migration-only drift toward v2.'
    $legacyReceiptValidate = & $launcherConvergenceScript `
        -Mode Validate `
        @launcherConvergenceParameters
    Assert-True (
        $legacyReceiptValidate.Result -ceq 'DRIFT' -and
        -not [bool]$legacyReceiptValidate.ReceiptCurrent -and
        [bool]$legacyReceiptValidate.ReceiptMigrationRequired -and
        -not [bool]$legacyReceiptValidate.MutationPerformed
    ) 'Launcher convergence Validate treated the v1 migration input as current.'
    $legacyReceiptMigration = & $launcherConvergenceScript `
        -Mode Apply `
        @launcherConvergenceParameters `
        -Confirm:$false
    Assert-True (
        $legacyReceiptMigration.Result -ceq 'PASS' -and
        [bool]$legacyReceiptMigration.ReceiptCurrent -and
        -not [bool]$legacyReceiptMigration.ReceiptMigrationRequired -and
        [string](Get-Content `
            -LiteralPath $launcherConvergence.ReceiptPath `
            -Raw | ConvertFrom-Json).schema -ceq
                'sq.librehw.launcher-convergence.v2'
    ) 'Launcher convergence did not migrate the v1 app-vendored receipt to v2.'

    $legacyAuthorityReceiptBytes = [System.IO.File]::ReadAllBytes(
        $launcherConvergence.CentralLauncherAuthorityReceiptPath)
    $legacyAuthorityReceiptText = [System.IO.File]::ReadAllText(
        $launcherConvergence.CentralLauncherAuthorityReceiptPath)
    try {
        $currentAuthorityReceipt = $legacyAuthorityReceiptText | ConvertFrom-Json
        $currentAuthorityReceipt.Destinations[0] | Add-Member `
            -NotePropertyName Existed `
            -NotePropertyValue $true
        $currentAuthorityReceipt.Destinations[0] | Add-Member `
            -NotePropertyName BeforeHash `
            -NotePropertyValue ('B' * 64)
        $currentAuthorityReceiptText =
            $currentAuthorityReceipt | ConvertTo-Json -Depth 8
        $currentAuthorityReceiptText | Set-Content `
            -LiteralPath $launcherConvergence.CentralLauncherAuthorityReceiptPath `
            -Encoding UTF8
        $currentAuthorityPlan = & $launcherConvergenceScript `
            -Mode Plan `
            @launcherConvergenceParameters
        Assert-True (
            [bool]$currentAuthorityPlan.CentralLauncherCurrent -and
            @($currentAuthorityPlan.BlockingIssues).Count -eq 0
        ) 'Launcher convergence rejected the current RunW v2 destination row.'

        $currentAuthorityReceipt.Operation = 'Relocated'
        $currentAuthorityReceipt | ConvertTo-Json -Depth 8 | Set-Content `
            -LiteralPath $launcherConvergence.CentralLauncherAuthorityReceiptPath `
            -Encoding UTF8
        $relocatedAuthorityPlan = & $launcherConvergenceScript `
            -Mode Plan `
            @launcherConvergenceParameters
        Assert-True (
            [bool]$relocatedAuthorityPlan.CentralLauncherCurrent -and
            @($relocatedAuthorityPlan.BlockingIssues).Count -eq 0
        ) 'Launcher convergence rejected a Relocated RunW v2 authority receipt.'

        $currentAuthorityTamperCases = @(
            [pscustomobject]@{ Name = 'extra property'; Mutate = {
                param($receipt)
                $receipt.Destinations[0] | Add-Member `
                    -NotePropertyName Unexpected `
                    -NotePropertyValue 'tamper'
            } },
            [pscustomobject]@{ Name = 'non-Boolean Existed'; Mutate = {
                param($receipt)
                $receipt.Destinations[0].Existed = 'true'
            } },
            [pscustomobject]@{ Name = 'invalid BeforeHash'; Mutate = {
                param($receipt)
                $receipt.Destinations[0].BeforeHash = 'tamper'
            } }
        )
        foreach ($tamperCase in $currentAuthorityTamperCases) {
            $tamperedCurrentAuthorityReceipt =
                $currentAuthorityReceiptText | ConvertFrom-Json
            & ([scriptblock]$tamperCase.Mutate) $tamperedCurrentAuthorityReceipt
            $tamperedCurrentAuthorityReceipt | ConvertTo-Json -Depth 8 |
                Set-Content `
                    -LiteralPath `
                        $launcherConvergence.CentralLauncherAuthorityReceiptPath `
                    -Encoding UTF8
            $tamperedCurrentAuthorityPlan = & $launcherConvergenceScript `
                -Mode Plan `
                @launcherConvergenceParameters
            Assert-True (
                $tamperedCurrentAuthorityPlan.Result -ceq 'DRIFT' -and
                @($tamperedCurrentAuthorityPlan.BlockingIssues).Count -gt 0
            ) "Launcher convergence trusted current RunW receipt tamper: $($tamperCase.Name)."
        }
    }
    finally {
        [System.IO.File]::WriteAllBytes(
            $launcherConvergence.CentralLauncherAuthorityReceiptPath,
            $legacyAuthorityReceiptBytes)
    }

    $authorityReceiptTamperCases = @(
        [pscustomobject]@{ Name = 'source root'; Mutate = {
            param($receipt, $fixture)
            $receipt.Source.Root = Join-Path $fixture.Root 'foreign-source'
        } },
        [pscustomobject]@{ Name = 'source commit'; Mutate = {
            param($receipt, $fixture)
            $receipt.Source.Commit = ('0' * 40)
        } },
        [pscustomobject]@{ Name = 'dirty source'; Mutate = {
            param($receipt, $fixture)
            $receipt.Source.Dirty = $true
        } },
        [pscustomobject]@{ Name = 'non-Boolean source dirt'; Mutate = {
            param($receipt, $fixture)
            $receipt.Source.Dirty = 'false'
        } },
        [pscustomobject]@{ Name = 'artifact path'; Mutate = {
            param($receipt, $fixture)
            $receipt.Artifact.Path = Join-Path $fixture.Root 'foreign-runw.exe'
        } },
        [pscustomobject]@{ Name = 'artifact version'; Mutate = {
            param($receipt, $fixture)
            $receipt.Artifact.Version = '1.3.0'
        } },
        [pscustomobject]@{ Name = 'self path'; Mutate = {
            param($receipt, $fixture)
            $receipt.ReceiptPath = Join-Path $fixture.Root 'foreign-receipt.json'
        } },
        [pscustomobject]@{ Name = 'destination path'; Mutate = {
            param($receipt, $fixture)
            $receipt.Destinations[0].Path = Join-Path $fixture.Root 'foreign-runw.exe'
        } },
        [pscustomobject]@{ Name = 'destination hash'; Mutate = {
            param($receipt, $fixture)
            $receipt.Destinations[0].Sha256 = ('0' * 64)
        } },
        [pscustomobject]@{ Name = 'exact destination set'; Mutate = {
            param($receipt, $fixture)
            $receipt.Destinations = @($receipt.Destinations) + [pscustomobject]@{
                Path = Join-Path $fixture.Root 'extra-runw.exe'
                Sha256 = [string]$receipt.Destinations[0].Sha256
            }
        } },
        [pscustomobject]@{ Name = 'artifact hash'; Mutate = {
            param($receipt, $fixture)
            $receipt.Artifact.Sha256 = ('0' * 64)
        } },
        [pscustomobject]@{ Name = 'artifact length'; Mutate = {
            param($receipt, $fixture)
            $receipt.Artifact.Length = [Int64]$receipt.Artifact.Length + 1
        } },
        [pscustomobject]@{ Name = 'operation'; Mutate = {
            param($receipt, $fixture)
            $receipt.Operation = 'Adopt'
        } }
    )
    $authorityCaseIndex = 0
    foreach ($tamperCase in $authorityReceiptTamperCases) {
        $authorityCaseIndex++
        $authorityFixture = New-LauncherConvergenceFixture `
            -Root $testRoot `
            -Name "authority-$authorityCaseIndex"
        $authorityReceipt = Get-Content `
            -LiteralPath $authorityFixture.CentralLauncherAuthorityReceiptPath `
            -Raw | ConvertFrom-Json
        & ([scriptblock]$tamperCase.Mutate) $authorityReceipt $authorityFixture
        $authorityReceipt | ConvertTo-Json -Depth 8 | Set-Content `
            -LiteralPath $authorityFixture.CentralLauncherAuthorityReceiptPath `
            -Encoding UTF8
        $authorityTamperPlan = & $launcherConvergenceScript `
            -Mode Plan `
            -CanonicalLauncherPath $authorityFixture.SourceLauncherPath `
            -CanonicalShimPath $authorityFixture.SourceShimPath `
            -RuntimeLauncherPath $authorityFixture.RuntimeLauncherPath `
            -RuntimeExecutablePath $authorityFixture.RuntimeExecutablePath `
            -PublicShimPath $authorityFixture.PublicShimPath `
            -CentralLauncherPath $authorityFixture.CentralLauncherPath `
            -CentralLauncherAuthorityReceiptPath `
                $authorityFixture.CentralLauncherAuthorityReceiptPath `
            -ReceiptPath $authorityFixture.ReceiptPath `
            -RollbackRoot $authorityFixture.RollbackRoot `
            -TestManagedTaskStatePath $authorityFixture.TaskStatePath `
            -TestIdentityVerifierPath $authorityFixture.IdentityVerifierPath `
            -NonLiveTestMode
        Assert-True (
            $authorityTamperPlan.Result -ceq 'DRIFT' -and
            @($authorityTamperPlan.BlockingIssues).Count -gt 0
        ) "Central RunW authority tamper was trusted: $($tamperCase.Name)."
    }

    $installedBytesFixture = New-LauncherConvergenceFixture `
        -Root $testRoot `
        -Name 'authority-installed-bytes'
    Add-Content `
        -LiteralPath $installedBytesFixture.CentralLauncherPath `
        -Value 'tamper'
    $installedBytesPlan = & $launcherConvergenceScript `
        -Mode Plan `
        -CanonicalLauncherPath $installedBytesFixture.SourceLauncherPath `
        -CanonicalShimPath $installedBytesFixture.SourceShimPath `
        -RuntimeLauncherPath $installedBytesFixture.RuntimeLauncherPath `
        -RuntimeExecutablePath $installedBytesFixture.RuntimeExecutablePath `
        -PublicShimPath $installedBytesFixture.PublicShimPath `
        -CentralLauncherPath $installedBytesFixture.CentralLauncherPath `
        -CentralLauncherAuthorityReceiptPath `
            $installedBytesFixture.CentralLauncherAuthorityReceiptPath `
        -ReceiptPath $installedBytesFixture.ReceiptPath `
        -RollbackRoot $installedBytesFixture.RollbackRoot `
        -TestManagedTaskStatePath $installedBytesFixture.TaskStatePath `
        -TestIdentityVerifierPath $installedBytesFixture.IdentityVerifierPath `
        -NonLiveTestMode
    Assert-True (
        $installedBytesPlan.Result -ceq 'DRIFT' -and
        @($installedBytesPlan.BlockingIssues).Count -gt 0
    ) 'Central RunW authority validation trusted modified installed bytes.'

    'tampered shim' |
        Set-Content -LiteralPath $launcherConvergence.PublicShimPath -Encoding ASCII
    $launcherRepairPlan = & $launcherConvergenceScript `
        -Mode Plan `
        @launcherConvergenceParameters
    Assert-True (
        $launcherRepairPlan.Result -ceq 'DRIFT' -and
        @($launcherRepairPlan.Issues) -match 'public shim'
    ) 'Launcher convergence Plan did not report public-shim drift.'
    $launcherRepair = & $launcherConvergenceScript `
        -Mode Apply `
        @launcherConvergenceParameters `
        -Confirm:$false
    Assert-True (
        $launcherRepair.Result -ceq 'PASS' -and
        (Get-LhmFileSha256 -Path $launcherConvergence.PublicShimPath) -ceq
            (Get-LhmFileSha256 -Path $launcherConvergence.SourceShimPath) -and
        @(Get-ChildItem -LiteralPath $launcherConvergence.RollbackRoot -Directory).Count -ge 1
    ) 'Launcher convergence did not repair drift with retained rollback evidence.'

    $baselineReceiptText =
        [System.IO.File]::ReadAllText($launcherConvergence.ReceiptPath)
    $receiptTamperCases = @(
        [pscustomobject]@{ Name = 'source launcher path'; Mutate = {
            param($receipt) $receipt.source.launcherPath = 'C:\foreign\launcher.ps1'
        } },
        [pscustomobject]@{ Name = 'source launcher hash'; Mutate = {
            param($receipt) $receipt.source.launcherSha256 = ('0' * 64)
        } },
        [pscustomobject]@{ Name = 'source shim path'; Mutate = {
            param($receipt) $receipt.source.shimPath = 'C:\foreign\librehw.cmd'
        } },
        [pscustomobject]@{ Name = 'source shim hash'; Mutate = {
            param($receipt) $receipt.source.shimSha256 = ('0' * 64)
        } },
        [pscustomobject]@{ Name = 'central launcher path'; Mutate = {
            param($receipt) $receipt.centralLauncher.path = 'C:\foreign\runw.exe'
        } },
        [pscustomobject]@{ Name = 'central launcher hash'; Mutate = {
            param($receipt) $receipt.centralLauncher.sha256 = ('0' * 64)
        } },
        [pscustomobject]@{ Name = 'central launcher authority path'; Mutate = {
            param($receipt) $receipt.centralLauncher.authorityReceiptPath = 'C:\foreign\receipt.json'
        } },
        [pscustomobject]@{ Name = 'central launcher authority hash'; Mutate = {
            param($receipt) $receipt.centralLauncher.authorityReceiptSha256 = ('0' * 64)
        } },
        [pscustomobject]@{ Name = 'managed task path'; Mutate = {
            param($receipt) $receipt.managedTask.path = '\Foreign\Task'
        } },
        [pscustomobject]@{ Name = 'managed task executable'; Mutate = {
            param($receipt) $receipt.managedTask.executablePath = 'C:\foreign\lhm.exe'
        } },
        [pscustomobject]@{ Name = 'managed task mutation flag'; Mutate = {
            param($receipt) $receipt.managedTask.validationOnly = $false
        } },
        [pscustomobject]@{ Name = 'managed task mutation flag string false'; Mutate = {
            param($receipt) $receipt.managedTask.validationOnly = 'false'
        } },
        [pscustomobject]@{ Name = 'managed task mutation flag numeric zero'; Mutate = {
            param($receipt) $receipt.managedTask.validationOnly = 0
        } },
        [pscustomobject]@{ Name = 'managed task mutation flag numeric one'; Mutate = {
            param($receipt) $receipt.managedTask.validationOnly = 1
        } },
        [pscustomobject]@{ Name = 'managed task mutation flag null'; Mutate = {
            param($receipt) $receipt.managedTask.validationOnly = $null
        } },
        [pscustomobject]@{ Name = 'rollback directory'; Mutate = {
            param($receipt) $receipt.rollbackDirectory = 'C:\foreign\rollback'
        } },
        [pscustomobject]@{ Name = 'rollback manifest path'; Mutate = {
            param($receipt) $receipt.rollbackManifestPath = 'C:\foreign\rollback.json'
        } },
        [pscustomobject]@{ Name = 'rollback manifest hash'; Mutate = {
            param($receipt) $receipt.rollbackManifestSha256 = ('0' * 64)
        } },
        [pscustomobject]@{ Name = 'exact target set'; Mutate = {
            param($receipt)
            $receipt.targets = @($receipt.targets) + [pscustomobject]@{
                role = 'foreign'
                path = 'C:\foreign\target'
                sha256 = ('0' * 64)
            }
        } }
    )
    foreach ($tamperCase in $receiptTamperCases) {
        $tamperedReceipt = $baselineReceiptText | ConvertFrom-Json
        $mutation = [scriptblock]$tamperCase.Mutate
        & $mutation $tamperedReceipt
        $tamperedReceipt | ConvertTo-Json -Depth 8 | Set-Content `
            -LiteralPath $launcherConvergence.ReceiptPath `
            -Encoding UTF8
        $tamperValidation = & $launcherConvergenceScript `
            -Mode Validate `
            @launcherConvergenceParameters
        Assert-True (
            $tamperValidation.Result -ceq 'DRIFT' -and
            -not [bool]$tamperValidation.ReceiptCurrent
        ) "Receipt tamper was trusted: $($tamperCase.Name)."
    }
    [System.IO.File]::WriteAllText(
        $launcherConvergence.ReceiptPath,
        $baselineReceiptText,
        [System.Text.UTF8Encoding]::new($false))

    $baselineReceipt = $baselineReceiptText | ConvertFrom-Json
    $rollbackManifestPath = [string]$baselineReceipt.rollbackManifestPath
    $baselineRollbackManifestBytes =
        [System.IO.File]::ReadAllBytes($rollbackManifestPath)
    Add-Content -LiteralPath $rollbackManifestPath -Value 'tamper'
    $manifestTamperValidation = & $launcherConvergenceScript `
        -Mode Validate `
        @launcherConvergenceParameters
    Assert-True (-not [bool]$manifestTamperValidation.ReceiptCurrent) `
        'Receipt validation trusted a tampered rollback manifest.'
    [System.IO.File]::WriteAllBytes(
        $rollbackManifestPath,
        $baselineRollbackManifestBytes)

    $rollbackManifest = Get-Content `
        -LiteralPath $rollbackManifestPath `
        -Raw | ConvertFrom-Json
    $priorReceiptPath = [string]$rollbackManifest.receiptBackup
    $priorReceiptText = [System.IO.File]::ReadAllText($priorReceiptPath)
    $priorReceipt = $priorReceiptText | ConvertFrom-Json
    $priorRollbackManifestPath = [string]$priorReceipt.rollbackManifestPath
    [System.IO.File]::WriteAllText(
        $launcherConvergence.ReceiptPath,
        $priorReceiptText,
        [System.Text.UTF8Encoding]::new($false))
    $priorReceiptValidation = & $launcherConvergenceScript `
        -Mode Validate `
        @launcherConvergenceParameters
    Assert-True (
        $priorReceiptValidation.Result -ceq 'PASS' -and
        [bool]$priorReceiptValidation.ReceiptCurrent
    ) 'Launcher convergence fixture did not retain a valid false-ownership receipt.'
    [System.IO.File]::WriteAllText(
        $launcherConvergence.ReceiptPath,
        $baselineReceiptText,
        [System.Text.UTF8Encoding]::new($false))

    $rollbackBooleanTamperCases = @(
        [pscustomobject]@{
            Name = 'rollback receipt ownership string false'
            ReceiptText = $baselineReceiptText
            ManifestPath = $rollbackManifestPath
            Field = 'receiptExisted'
            Value = 'false'
        },
        [pscustomobject]@{
            Name = 'rollback receipt ownership numeric one'
            ReceiptText = $baselineReceiptText
            ManifestPath = $rollbackManifestPath
            Field = 'receiptExisted'
            Value = 1
        },
        [pscustomobject]@{
            Name = 'rollback receipt ownership numeric zero'
            ReceiptText = $priorReceiptText
            ManifestPath = $priorRollbackManifestPath
            Field = 'receiptExisted'
            Value = 0
        },
        [pscustomobject]@{
            Name = 'rollback receipt ownership null'
            ReceiptText = $priorReceiptText
            ManifestPath = $priorRollbackManifestPath
            Field = 'receiptExisted'
            Value = $null
        },
        [pscustomobject]@{
            Name = 'rollback file ownership string false'
            ReceiptText = $baselineReceiptText
            ManifestPath = $rollbackManifestPath
            Field = 'fileExisted'
            Value = 'false'
        },
        [pscustomobject]@{
            Name = 'rollback file ownership numeric one'
            ReceiptText = $baselineReceiptText
            ManifestPath = $rollbackManifestPath
            Field = 'fileExisted'
            Value = 1
        },
        [pscustomobject]@{
            Name = 'rollback file ownership numeric zero'
            ReceiptText = $priorReceiptText
            ManifestPath = $priorRollbackManifestPath
            Field = 'fileExisted'
            Value = 0
        },
        [pscustomobject]@{
            Name = 'rollback file ownership null'
            ReceiptText = $priorReceiptText
            ManifestPath = $priorRollbackManifestPath
            Field = 'fileExisted'
            Value = $null
        }
    )
    foreach ($tamperCase in $rollbackBooleanTamperCases) {
        $tamperedManifestPath = [string]$tamperCase.ManifestPath
        $originalManifestBytes =
            [System.IO.File]::ReadAllBytes($tamperedManifestPath)
        try {
            $tamperedManifest =
                [System.IO.File]::ReadAllText($tamperedManifestPath) |
                ConvertFrom-Json
            if ([string]$tamperCase.Field -ceq 'receiptExisted') {
                $tamperedManifest.receiptExisted = $tamperCase.Value
            }
            else {
                @($tamperedManifest.files)[0].existed = $tamperCase.Value
            }
            $tamperedManifest | ConvertTo-Json -Depth 8 | Set-Content `
                -LiteralPath $tamperedManifestPath `
                -Encoding UTF8

            $tamperedReceipt = [string]$tamperCase.ReceiptText | ConvertFrom-Json
            $tamperedReceipt.rollbackManifestSha256 =
                Get-LhmFileSha256 -Path $tamperedManifestPath
            $tamperedReceipt | ConvertTo-Json -Depth 8 | Set-Content `
                -LiteralPath $launcherConvergence.ReceiptPath `
                -Encoding UTF8
            $tamperValidation = & $launcherConvergenceScript `
                -Mode Validate `
                @launcherConvergenceParameters
            Assert-True (
                $tamperValidation.Result -ceq 'DRIFT' -and
                -not [bool]$tamperValidation.ReceiptCurrent
            ) "Rollback Boolean tamper was trusted: $($tamperCase.Name)."
        }
        finally {
            [System.IO.File]::WriteAllBytes(
                $tamperedManifestPath,
                $originalManifestBytes)
            [System.IO.File]::WriteAllText(
                $launcherConvergence.ReceiptPath,
                $baselineReceiptText,
                [System.Text.UTF8Encoding]::new($false))
        }
    }

    $rollbackBackup = @($rollbackManifest.files | Where-Object existed)[0]
    $baselineRollbackBackupBytes =
        [System.IO.File]::ReadAllBytes([string]$rollbackBackup.backupPath)
    Add-Content -LiteralPath ([string]$rollbackBackup.backupPath) -Value 'tamper'
    $backupTamperValidation = & $launcherConvergenceScript `
        -Mode Validate `
        @launcherConvergenceParameters
    Assert-True (-not [bool]$backupTamperValidation.ReceiptCurrent) `
        'Receipt validation trusted a tampered rollback backup.'
    [System.IO.File]::WriteAllBytes(
        [string]$rollbackBackup.backupPath,
        $baselineRollbackBackupBytes)

    $activeRollbackDirectory = [string]$baselineReceipt.rollbackDirectory
    $missingRollbackDirectory = "$activeRollbackDirectory.missing"
    Move-Item -LiteralPath $activeRollbackDirectory -Destination $missingRollbackDirectory
    try {
        $missingRollbackValidation = & $launcherConvergenceScript `
            -Mode Validate `
            @launcherConvergenceParameters
        Assert-True (-not [bool]$missingRollbackValidation.ReceiptCurrent) `
            'Receipt validation trusted a missing rollback directory.'
    }
    finally {
        Move-Item `
            -LiteralPath $missingRollbackDirectory `
            -Destination $activeRollbackDirectory
    }

    for ($retentionCase = 0; $retentionCase -lt 4; $retentionCase++) {
        "retention tamper $retentionCase" | Set-Content `
            -LiteralPath $launcherConvergence.PublicShimPath `
            -Encoding ASCII
        $null = & $launcherConvergenceScript `
            -Mode Apply `
            @launcherConvergenceParameters `
            -Confirm:$false
    }
    Assert-True (
        @(Get-ChildItem `
            -LiteralPath $launcherConvergence.RollbackRoot `
            -Directory).Count -le 2
    ) 'Launcher convergence did not enforce bounded rollback retention.'

    $activeReceiptAfterRetention = Get-Content `
        -LiteralPath $launcherConvergence.ReceiptPath `
        -Raw | ConvertFrom-Json
    $protectedRollbackDirectory =
        [string]$activeReceiptAfterRetention.rollbackDirectory
    'pre-apply drift retained across failures' | Set-Content `
        -LiteralPath $launcherConvergence.PublicShimPath `
        -Encoding ASCII
    for ($failedRetentionCase = 0; $failedRetentionCase -lt 3; $failedRetentionCase++) {
        Assert-Throws -MessagePattern 'AfterLauncherDeployment' -Action {
            $null = & $launcherConvergenceScript `
                -Mode Apply `
                @launcherConvergenceParameters `
                -TestFailurePoint AfterLauncherDeployment `
                -Confirm:$false
        }
    }
    Assert-True (
        (Test-Path -LiteralPath $protectedRollbackDirectory -PathType Container) -and
        @(Get-ChildItem `
            -LiteralPath $launcherConvergence.RollbackRoot `
            -Directory).Count -le 2
    ) 'Rollback retention pruned the packet owned by the active receipt.'

    $badIdentityConvergence = New-LauncherConvergenceFixture `
        -Root $testRoot `
        -Name 'bad-identity' `
        -BadIdentity
    $badIdentitySignature = Get-TreeSignature -Root $badIdentityConvergence.Root
    Assert-Throws -MessagePattern 'restricted to verified machine' -Action {
        $null = & $launcherConvergenceScript `
            -Mode Apply `
            -CanonicalLauncherPath $badIdentityConvergence.SourceLauncherPath `
            -CanonicalShimPath $badIdentityConvergence.SourceShimPath `
            -RuntimeLauncherPath $badIdentityConvergence.RuntimeLauncherPath `
            -RuntimeExecutablePath $badIdentityConvergence.RuntimeExecutablePath `
            -PublicShimPath $badIdentityConvergence.PublicShimPath `
            -CentralLauncherPath $badIdentityConvergence.CentralLauncherPath `
            -CentralLauncherAuthorityReceiptPath `
                $badIdentityConvergence.CentralLauncherAuthorityReceiptPath `
            -ReceiptPath $badIdentityConvergence.ReceiptPath `
            -RollbackRoot $badIdentityConvergence.RollbackRoot `
            -TestManagedTaskStatePath $badIdentityConvergence.TaskStatePath `
            -TestIdentityVerifierPath $badIdentityConvergence.IdentityVerifierPath `
            -NonLiveTestMode `
            -Confirm:$false
    }
    Assert-True (
        (Get-TreeSignature -Root $badIdentityConvergence.Root) -ceq
            $badIdentitySignature
    ) 'Rejected launcher identity changed the non-live fixture.'

    foreach ($failurePoint in @(
        'AfterLauncherDeployment',
        'AfterPublicShimDeployment',
        'AfterReceiptDeployment'
    )) {
        $atomicConvergence = New-LauncherConvergenceFixture `
            -Root $testRoot `
            -Name "atomic-$failurePoint" `
            -SeedDestinations
        $atomicLauncherHash =
            Get-LhmFileSha256 -Path $atomicConvergence.RuntimeLauncherPath
        $atomicCentralLauncherHash =
            Get-LhmFileSha256 -Path $atomicConvergence.CentralLauncherPath
        $atomicShimHash = Get-LhmFileSha256 -Path $atomicConvergence.PublicShimPath
        $atomicTaskHash = Get-LhmFileSha256 -Path $atomicConvergence.TaskStatePath
        Assert-Throws -MessagePattern $failurePoint -Action {
            $null = & $launcherConvergenceScript `
                -Mode Apply `
                -CanonicalLauncherPath $atomicConvergence.SourceLauncherPath `
                -CanonicalShimPath $atomicConvergence.SourceShimPath `
                -RuntimeLauncherPath $atomicConvergence.RuntimeLauncherPath `
                -RuntimeExecutablePath $atomicConvergence.RuntimeExecutablePath `
                -PublicShimPath $atomicConvergence.PublicShimPath `
                -CentralLauncherPath $atomicConvergence.CentralLauncherPath `
                -CentralLauncherAuthorityReceiptPath `
                    $atomicConvergence.CentralLauncherAuthorityReceiptPath `
                -ReceiptPath $atomicConvergence.ReceiptPath `
                -RollbackRoot $atomicConvergence.RollbackRoot `
                -TestManagedTaskStatePath $atomicConvergence.TaskStatePath `
                -TestIdentityVerifierPath $atomicConvergence.IdentityVerifierPath `
                -TestFailurePoint $failurePoint `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-LhmFileSha256 -Path $atomicConvergence.RuntimeLauncherPath) -ceq
                $atomicLauncherHash -and
            (Get-LhmFileSha256 -Path $atomicConvergence.CentralLauncherPath) -ceq
                $atomicCentralLauncherHash -and
            -not (Test-Path `
                -LiteralPath $atomicConvergence.LegacyVendoredLauncherPath `
                -PathType Leaf) -and
            (Get-LhmFileSha256 -Path $atomicConvergence.PublicShimPath) -ceq
                $atomicShimHash -and
            (Get-LhmFileSha256 -Path $atomicConvergence.TaskStatePath) -ceq
                $atomicTaskHash -and
            -not (Test-Path -LiteralPath $atomicConvergence.ReceiptPath)
        ) "Injected $failurePoint failure did not restore every deployed target."
    }

    $rollbackFailureConvergence = New-LauncherConvergenceFixture `
        -Root $testRoot `
        -Name 'rollback-failure-reporting' `
        -SeedDestinations
    $rollbackFailureMessage = $null
    try {
        $null = & $launcherConvergenceScript `
            -Mode Apply `
            -CanonicalLauncherPath $rollbackFailureConvergence.SourceLauncherPath `
            -CanonicalShimPath $rollbackFailureConvergence.SourceShimPath `
            -RuntimeLauncherPath $rollbackFailureConvergence.RuntimeLauncherPath `
            -RuntimeExecutablePath $rollbackFailureConvergence.RuntimeExecutablePath `
            -PublicShimPath $rollbackFailureConvergence.PublicShimPath `
            -CentralLauncherPath $rollbackFailureConvergence.CentralLauncherPath `
            -CentralLauncherAuthorityReceiptPath `
                $rollbackFailureConvergence.CentralLauncherAuthorityReceiptPath `
            -ReceiptPath $rollbackFailureConvergence.ReceiptPath `
            -RollbackRoot $rollbackFailureConvergence.RollbackRoot `
            -TestManagedTaskStatePath $rollbackFailureConvergence.TaskStatePath `
            -TestIdentityVerifierPath $rollbackFailureConvergence.IdentityVerifierPath `
            -TestCorruptRollbackBackupRole launcher `
            -TestFailurePoint AfterLauncherDeployment `
            -NonLiveTestMode `
            -Confirm:$false
    }
    catch {
        $rollbackFailureMessage = $_.Exception.Message
    }
    Assert-True (
        $rollbackFailureMessage -match 'AfterLauncherDeployment' -and
        $rollbackFailureMessage -match 'Rollback also failed' -and
        $rollbackFailureMessage -match "Target 'launcher'"
    ) 'Launcher convergence did not report original and rollback failures separately.'

    if ($LauncherConvergenceOnly) {
        [pscustomobject]@{
            Result = 'PASS'
            TestRoot = $testRoot
            LauncherConvergenceCases = 25
            WindowsPowerShellLauncherShimCompatibility = $true
        }
        return
    }

    Assert-True (
        $script:LhmProductionInstallRoot -ceq 'E:\Monitoring\LibreHW\Runtime' -and
        $script:LhmLegacyProductionInstallRoot -ceq 'E:\SQ_HQ\Monitoring\LibreHW' -and
        $script:LhmPreviousProductionDataRoot -ceq
            'E:\SQ_HQ\sqprofile\sqdata\LibreHardwareMonitor' -and
        $script:LhmProductionDataRootVariable -ceq 'SEV_LOCAL_DATA' -and
        $script:LhmProductionBinRootVariable -ceq 'SEV_LOCAL_BIN' -and
        $script:LhmProductionDataRelativePath -ceq 'LibreHardwareMonitor' -and
        $script:LhmProductionPublicShimName -ceq 'librehw.cmd' -and
        (Get-LhmPublicShimCentralLauncherToken) -ceq
            '%SEV_LOCAL_BIN%\runw\runw.exe' -and
        $script:LhmProductionHealthUri -ceq 'http://localhost:8085/data.json' -and
        $script:LhmExpectedInstanceId -ceq
            'ca96d510-7d87-4cec-8e1a-bd8fc3866903' -and
        $script:LhmPreRelocationLauncherSha256 -ceq
            '74148efe09a18cb00047d3c6915153717071c9a5e3c1198193caeb0f0dac63c0' -and
        $script:LhmManagedTaskPrincipalSid -ceq
            'S-1-5-21-3033086598-3000262358-161002696-1001' -and
        $script:LhmManagedTaskPrincipalUserId -ceq 'Sev' -and
        $script:LhmManagedTaskLogonUserId -ceq 'SND-Desk\Sev' -and
        $script:LhmLegacyRootTaskExecutablePath -ceq
            'E:\Monitoring\sq-librehw\bin\Release\net10.0-windows\LibreHardwareMonitor.Windows.Forms.exe' -and
        $script:LhmLauncherTargetPath -ceq
            'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1' -and
        $script:LhmLegacyLauncherTargetPath -ceq
            'E:\UserProfile\script-data\Start-LibreHardwareMonitor.ps1'
    ) 'Production roots do not match the reviewed runtime-migration contract.'
    $forbiddenCurrentRoots = @(
        'E:\SevLocal\Data\LibreHardwareMonitor',
        'E:\SevLocal\Bin\librehw.cmd',
        'E:\SevLocal\Bin\runw\runw.exe',
        'E:\Data\LibreHardwareMonitor',
        'E:\Bin\librehw.cmd',
        'E:\Bin\runw\runw.exe'
    )
    $currentAuthorityScripts = @(
        $commonScript,
        $installScript,
        $rollbackScript,
        $relocationScript,
        $canonicalLauncher,
        $canonicalPublicShim,
        $launcherConvergenceScript,
        $finalizeScript
    )
    foreach ($authorityScript in $currentAuthorityScripts) {
        $authorityText = [System.IO.File]::ReadAllText($authorityScript)
        foreach ($forbiddenRoot in $forbiddenCurrentRoots) {
            Assert-True ($authorityText -notmatch [regex]::Escape($forbiddenRoot)) `
                "'$authorityScript' still hardcodes current root '$forbiddenRoot'."
        }
    }
    foreach ($releaseScript in @($installScript, $rollbackScript, $relocationScript)) {
        $releaseText = Get-Content -LiteralPath $releaseScript -Raw
        Assert-True (
            $releaseText -match [regex]::Escape(
                "[string] `$InstallRoot = 'E:\Monitoring\LibreHW\Runtime'") -and
            $releaseText -match [regex]::Escape('Get-LhmProductionDataRoot') -and
            $releaseText -match [regex]::Escape('Get-LhmProductionPublicShimPath') -and
            $releaseText -match [regex]::Escape(
                "'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1'")
        ) "Production defaults drifted in '$releaseScript'."
    }
    $launcherText = Get-Content -LiteralPath $canonicalLauncher -Raw
    Assert-True (
        $launcherText -match [regex]::Escape(
            "`$InstallRoot = 'E:\Monitoring\LibreHW\Runtime'") -and
        $launcherText -match [regex]::Escape("'SEV_LOCAL_DATA'") -and
        $launcherText -match [regex]::Escape('Get-LauncherProductionDataRoot')
    ) 'Canonical launcher does not target the stable runtime and env-var data root.'

    $fakeEnvironmentValues = @{
        MACHINE_TOOLS_ROOT = 'X:\'
        SEV_LOCAL_ROOT = '%MACHINE_TOOLS_ROOT%FakeLocal'
        SEV_LOCAL_DATA = '%SEV_LOCAL_ROOT%\Data'
        CYCLE_A = '%CYCLE_B%'
        CYCLE_B = '%CYCLE_A%'
    }
    $fakeResolver = { param($TokenName) $fakeEnvironmentValues[$TokenName] }.GetNewClosure()
    Assert-True (
        (Expand-LhmPersistedEnvironmentTemplate `
            -Name 'SEV_LOCAL_DATA' `
            -Value $fakeEnvironmentValues.SEV_LOCAL_DATA `
            -RawValueResolver $fakeResolver) -ceq 'X:\FakeLocal\Data'
    ) 'Chained persisted templates did not expand through the drive-root separator verbatim.'
    Assert-True (
        (Expand-LhmPersistedEnvironmentTemplate `
            -Name 'SEV_LOCAL_PLAIN' `
            -Value 'X:\Plain' `
            -RawValueResolver $fakeResolver) -ceq 'X:\Plain'
    ) 'A template-free persisted value must expand to itself.'
    Assert-Throws {
        Expand-LhmPersistedEnvironmentTemplate `
            -Name 'SEV_LOCAL_DATA' `
            -Value '%LHM_MISSING_TOKEN%\Data' `
            -RawValueResolver $fakeResolver
    } "references '%LHM_MISSING_TOKEN%'"
    Assert-Throws {
        Expand-LhmPersistedEnvironmentTemplate `
            -Name 'CYCLE_A' `
            -Value $fakeEnvironmentValues.CYCLE_A `
            -RawValueResolver $fakeResolver
    } 'did not fully expand after 8 passes'

    $processData = [Environment]::GetEnvironmentVariable('SEV_LOCAL_DATA', 'Process')
    $userData = [Environment]::GetEnvironmentVariable('SEV_LOCAL_DATA', 'User')
    $machineData = [Environment]::GetEnvironmentVariable('SEV_LOCAL_DATA', 'Machine')
    if (-not [string]::IsNullOrWhiteSpace($userData) -or
        -not [string]::IsNullOrWhiteSpace($machineData)) {
        try {
            [Environment]::SetEnvironmentVariable(
                'SEV_LOCAL_DATA',
                'C:\wrong-lhm-data',
                'Process')
            $resolvedData = Get-LhmPersistedEnvironmentValue -Name 'SEV_LOCAL_DATA'
            Assert-True ($resolvedData -cne 'C:\wrong-lhm-data') `
                'Data-root resolution preferred a stale Process value over persisted User/Machine.'
            Assert-True ($resolvedData -notmatch '%') `
                'Data-root resolution returned an unexpanded %...% template.'
            Assert-True ($resolvedData -match '^(?:[A-Za-z]:[\\/]|\\\\)') `
                "Data-root resolution returned non-absolute path '$resolvedData'."
            Assert-True (Test-Path -LiteralPath $resolvedData -PathType Container) `
                "Persisted SEV_LOCAL_DATA root '$resolvedData' does not exist."
            Assert-True (
                (Test-LhmPathEqual `
                    -Left (Get-LhmProductionDataRoot) `
                    -Right (Join-Path $resolvedData 'LibreHardwareMonitor'))
            ) 'Production data root did not join SEV_LOCAL_DATA with LibreHardwareMonitor.'
        }
        finally {
            [Environment]::SetEnvironmentVariable(
                'SEV_LOCAL_DATA',
                $processData,
                'Process')
        }
    }

    $processBin = [Environment]::GetEnvironmentVariable('SEV_LOCAL_BIN', 'Process')
    $userBin = [Environment]::GetEnvironmentVariable('SEV_LOCAL_BIN', 'User')
    $machineBin = [Environment]::GetEnvironmentVariable('SEV_LOCAL_BIN', 'Machine')
    if (-not [string]::IsNullOrWhiteSpace($userBin) -or
        -not [string]::IsNullOrWhiteSpace($machineBin)) {
        try {
            [Environment]::SetEnvironmentVariable(
                'SEV_LOCAL_BIN',
                'C:\wrong-lhm-bin',
                'Process')
            $resolvedBin = Get-LhmPersistedEnvironmentValue -Name 'SEV_LOCAL_BIN'
            Assert-True ($resolvedBin -cne 'C:\wrong-lhm-bin') `
                'Bin-root resolution preferred a stale Process value over persisted User/Machine.'
            Assert-True ($resolvedBin -notmatch '%') `
                'Bin-root resolution returned an unexpanded %...% template.'
            Assert-True ($resolvedBin -match '^(?:[A-Za-z]:[\\/]|\\\\)') `
                "Bin-root resolution returned non-absolute path '$resolvedBin'."
            Assert-True (Test-Path -LiteralPath $resolvedBin -PathType Container) `
                "Persisted SEV_LOCAL_BIN root '$resolvedBin' does not exist."
            Assert-True (
                (Test-LhmPathEqual `
                    -Left (Get-LhmProductionPublicShimPath) `
                    -Right (Join-Path $resolvedBin 'librehw.cmd'))
            ) 'Production public shim path did not join SEV_LOCAL_BIN with librehw.cmd.'
        }
        finally {
            [Environment]::SetEnvironmentVariable(
                'SEV_LOCAL_BIN',
                $processBin,
                'Process')
        }
    }

    $finalizerText = Get-Content -LiteralPath $finalizeScript -Raw
    Assert-True (
        $finalizerText -match [regex]::Escape(
            '%MACHINE_TOOLS_ROOT%\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1')
    ) 'Finalizer no longer requires the app-owned public-shim launcher path.'

    $runtimeMigrationText = Get-Content -LiteralPath $runtimeMigrationScript -Raw
    foreach ($expectedLiteral in @(
        "[string] `$LegacyInstallRoot = 'E:\SQ_HQ\Monitoring\LibreHW'",
        "[string] `$InstallRoot = 'E:\Monitoring\LibreHW\Runtime'",
        "'E:\UserProfile\script-data\Start-LibreHardwareMonitor.ps1'",
        "'E:\Monitoring\LibreHW\Scripts\Start-LibreHardwareMonitor.ps1'",
        'Get-LhmProductionPublicShimPath'
    )) {
        Assert-True ($runtimeMigrationText -match [regex]::Escape($expectedLiteral)) `
            "Runtime migration default is missing '$expectedLiteral'."
    }

    $runtimeMigration = New-RuntimeRootMigrationFixture `
        -Root $testRoot `
        -Name 'success' `
        -CandidateDirectory $candidate1
    $runtimeMigrationPlan = & $runtimeMigrationScript `
        -Mode Plan `
        -LegacyInstallRoot $runtimeMigration.LegacyInstallRoot `
        -InstallRoot $runtimeMigration.InstallRoot `
        -DataRoot $runtimeMigration.DataRoot `
        -LegacyLauncherPath $runtimeMigration.LegacyLauncherPath `
        -LauncherTargetPath $runtimeMigration.LauncherTargetPath `
        -PublicShimPath $runtimeMigration.PublicShimPath `
        -HideLaunchArtifactPath $runtimeMigration.HideLaunchArtifactPath `
        -HideLaunchAuthorityReceiptPath $runtimeMigration.HideLaunchAuthorityReceiptPath `
        -TestExternalTaskStatePath $runtimeMigration.TaskStatePath `
        -NonLiveTestMode
    Assert-True (
        $runtimeMigrationPlan.Result -ceq 'PASS' -and
        $runtimeMigrationPlan.LegacyRuntimePresent -and
        -not $runtimeMigrationPlan.MigratedRuntimePresent -and
        [bool]$runtimeMigrationPlan.HideLaunchCurrent -and
        -not $runtimeMigrationPlan.MutationPerformed
    ) 'Runtime migration plan did not report the legacy fixture accurately.'

    $runtimeMigrationResult = & $runtimeMigrationScript `
        -Mode Apply `
        -LegacyInstallRoot $runtimeMigration.LegacyInstallRoot `
        -InstallRoot $runtimeMigration.InstallRoot `
        -DataRoot $runtimeMigration.DataRoot `
        -LegacyLauncherPath $runtimeMigration.LegacyLauncherPath `
        -LauncherTargetPath $runtimeMigration.LauncherTargetPath `
        -PublicShimPath $runtimeMigration.PublicShimPath `
        -HideLaunchArtifactPath $runtimeMigration.HideLaunchArtifactPath `
        -HideLaunchAuthorityReceiptPath $runtimeMigration.HideLaunchAuthorityReceiptPath `
        -TestExternalTaskStatePath $runtimeMigration.TaskStatePath `
        -NonLiveTestMode `
        -Confirm:$false
    Assert-True (
        $runtimeMigrationResult.Result -ceq 'PASS' -and
        $runtimeMigrationResult.MutationPerformed -and
        (Test-Path -LiteralPath $runtimeMigration.InstallRoot -PathType Container) -and
        -not (Test-Path -LiteralPath $runtimeMigration.LegacyInstallRoot) -and
        (Test-Path -LiteralPath $runtimeMigration.LauncherTargetPath -PathType Leaf) -and
        (Get-LhmFileSha256 -Path $runtimeMigration.RuntimeHideLaunchPath) -ceq
            (Get-LhmFileSha256 -Path $runtimeMigration.HideLaunchArtifactPath) -and
        -not (Test-Path -LiteralPath $runtimeMigration.LegacyLauncherPath) -and
        (Test-Path -LiteralPath $runtimeMigration.RecoveryRoot -PathType Container)
    ) 'Runtime migration did not converge the success fixture.'
    $runtimeMigrationTask = Get-Content `
        -LiteralPath $runtimeMigration.TaskStatePath `
        -Raw | ConvertFrom-Json
    Assert-True (
        (Test-LhmPathEqual `
            -Left ([string]$runtimeMigrationTask.execute) `
            -Right (Join-Path `
                $runtimeMigration.InstallRoot `
                $script:LhmExecutableName)) -and
        (Test-LhmPathEqual `
            -Left ([string]$runtimeMigrationTask.workingDirectory) `
            -Right $runtimeMigration.InstallRoot)
    ) 'Runtime migration did not rebind the managed task fixture.'
    $null = & $runtimeMigrationScript `
        -Mode Validate `
        -LegacyInstallRoot $runtimeMigration.LegacyInstallRoot `
        -InstallRoot $runtimeMigration.InstallRoot `
        -DataRoot $runtimeMigration.DataRoot `
        -LegacyLauncherPath $runtimeMigration.LegacyLauncherPath `
        -LauncherTargetPath $runtimeMigration.LauncherTargetPath `
        -PublicShimPath $runtimeMigration.PublicShimPath `
        -HideLaunchArtifactPath $runtimeMigration.HideLaunchArtifactPath `
        -HideLaunchAuthorityReceiptPath $runtimeMigration.HideLaunchAuthorityReceiptPath `
        -TestExternalTaskStatePath $runtimeMigration.TaskStatePath `
        -NonLiveTestMode

    'tampered migration hidelaunch' | Set-Content `
        -LiteralPath $runtimeMigration.RuntimeHideLaunchPath `
        -Encoding ASCII
    Assert-Throws -MessagePattern 'app-vendored HideLaunch' -Action {
        $null = & $runtimeMigrationScript `
            -Mode Validate `
            -LegacyInstallRoot $runtimeMigration.LegacyInstallRoot `
            -InstallRoot $runtimeMigration.InstallRoot `
            -DataRoot $runtimeMigration.DataRoot `
            -LegacyLauncherPath $runtimeMigration.LegacyLauncherPath `
            -LauncherTargetPath $runtimeMigration.LauncherTargetPath `
            -PublicShimPath $runtimeMigration.PublicShimPath `
            -HideLaunchArtifactPath $runtimeMigration.HideLaunchArtifactPath `
            -HideLaunchAuthorityReceiptPath $runtimeMigration.HideLaunchAuthorityReceiptPath `
            -TestExternalTaskStatePath $runtimeMigration.TaskStatePath `
            -NonLiveTestMode
    }

    $missingHideMigration = New-RuntimeRootMigrationFixture `
        -Root $testRoot `
        -Name 'missing-hide' `
        -CandidateDirectory $candidate1
    Remove-Item -LiteralPath $missingHideMigration.RuntimeHideLaunchPath -Force
    Assert-Throws -MessagePattern 'app-vendored HideLaunch' -Action {
        $null = & $runtimeMigrationScript `
            -Mode Apply `
            -LegacyInstallRoot $missingHideMigration.LegacyInstallRoot `
            -InstallRoot $missingHideMigration.InstallRoot `
            -DataRoot $missingHideMigration.DataRoot `
            -LegacyLauncherPath $missingHideMigration.LegacyLauncherPath `
            -LauncherTargetPath $missingHideMigration.LauncherTargetPath `
            -PublicShimPath $missingHideMigration.PublicShimPath `
            -HideLaunchArtifactPath $missingHideMigration.HideLaunchArtifactPath `
            -HideLaunchAuthorityReceiptPath $missingHideMigration.HideLaunchAuthorityReceiptPath `
            -TestExternalTaskStatePath $missingHideMigration.TaskStatePath `
            -NonLiveTestMode `
            -Confirm:$false
    }
    Assert-True (
        (Test-Path -LiteralPath $missingHideMigration.LegacyInstallRoot) -and
        -not (Test-Path -LiteralPath $missingHideMigration.InstallRoot)
    ) 'Missing HideLaunch did not fail closed before runtime migration mutation.'

    $failedRuntimeMigration = New-RuntimeRootMigrationFixture `
        -Root $testRoot `
        -Name 'rollback' `
        -CandidateDirectory $candidate1
    $failedShimHash = Get-LhmFileSha256 -Path $failedRuntimeMigration.PublicShimPath
    $failedTaskHash = Get-LhmFileSha256 -Path $failedRuntimeMigration.TaskStatePath
    Assert-Throws -MessagePattern 'AfterBindings' -Action {
        $null = & $runtimeMigrationScript `
            -Mode Apply `
            -LegacyInstallRoot $failedRuntimeMigration.LegacyInstallRoot `
            -InstallRoot $failedRuntimeMigration.InstallRoot `
            -DataRoot $failedRuntimeMigration.DataRoot `
            -LegacyLauncherPath $failedRuntimeMigration.LegacyLauncherPath `
            -LauncherTargetPath $failedRuntimeMigration.LauncherTargetPath `
            -PublicShimPath $failedRuntimeMigration.PublicShimPath `
            -HideLaunchArtifactPath $failedRuntimeMigration.HideLaunchArtifactPath `
            -HideLaunchAuthorityReceiptPath $failedRuntimeMigration.HideLaunchAuthorityReceiptPath `
            -TestExternalTaskStatePath $failedRuntimeMigration.TaskStatePath `
            -NonLiveTestMode `
            -TestFailurePoint AfterBindings `
            -Confirm:$false
    }
    Assert-True (
        (Test-Path -LiteralPath $failedRuntimeMigration.LegacyInstallRoot -PathType Container) -and
        -not (Test-Path -LiteralPath $failedRuntimeMigration.InstallRoot) -and
        (Test-Path -LiteralPath $failedRuntimeMigration.LegacyLauncherPath -PathType Leaf) -and
        -not (Test-Path -LiteralPath $failedRuntimeMigration.LauncherTargetPath) -and
        (Get-LhmFileSha256 -Path $failedRuntimeMigration.PublicShimPath) -ceq
            $failedShimHash -and
        (Get-LhmFileSha256 -Path $failedRuntimeMigration.TaskStatePath) -ceq
            $failedTaskHash
    ) 'Runtime migration did not roll back the injected binding failure.'

    $relocation = New-DataRootRelocationFixture `
        -Root $testRoot `
        -Name 'success' `
        -CandidateDirectory $candidate1
    $relocationBeforeWhatIf = Get-TreeSignature -Root $relocation.Root
    $null = & $relocationScript `
        -InstallRoot $relocation.InstallRoot `
        -SourceDataRoot $relocation.SourceDataRoot `
        -DataRoot $relocation.DataRoot `
        -LauncherTargetPath $relocation.LauncherTarget `
        -PublicShimPath $relocation.ShimPath `
        -TestExternalStateRoot $relocation.ExternalRoot `
        -DataMoveAlreadyCompleted `
        -NonLiveTestMode `
        -WhatIf
    Assert-True (
        (Get-TreeSignature -Root $relocation.Root) -ceq $relocationBeforeWhatIf
    ) 'Relocation -WhatIf changed its non-live fixture.'
    $relocationShimHash = Get-LhmFileSha256 -Path $relocation.ShimPath
    $preStableSignature = Get-TreeSignature -Root $relocation.PreStableRecoveryRoot
    $relocationResult = & $relocationScript `
        -InstallRoot $relocation.InstallRoot `
        -SourceDataRoot $relocation.SourceDataRoot `
        -DataRoot $relocation.DataRoot `
        -LauncherTargetPath $relocation.LauncherTarget `
        -PublicShimPath $relocation.ShimPath `
        -TestExternalStateRoot $relocation.ExternalRoot `
        -DataMoveAlreadyCompleted `
        -NonLiveTestMode `
        -Confirm:$false
    Assert-True (
        $relocationResult.Result -ceq 'PASS' -and
        -not [bool]$relocationResult.AlreadyConverged -and
        -not [bool]$relocationResult.Activated -and
        [bool]$relocationResult.TestMode
    ) 'Non-live data-root relocation did not report a successful first convergence.'
    $null = Read-LhmRuntimeConfig `
        -Path (Join-Path $relocation.InstallRoot $script:LhmRuntimeConfigName) `
        -ExpectedDataRoot $relocation.DataRoot `
        -ExpectedManagedTaskPath $script:LhmManagedTaskPath
    Assert-True (
        (Get-LhmFileSha256 -Path $relocation.LauncherTarget) -ceq
            (Get-LhmFileSha256 -Path $canonicalLauncher)
    ) 'Relocation did not deploy the canonical launcher content.'
    $relocatedTask = Get-Content `
        -LiteralPath (Join-Path $relocation.ExternalRoot 'managed-task.json') `
        -Raw | ConvertFrom-Json
    Assert-True ([bool]$relocatedTask.enabled) `
        'Relocation did not enable the existing managed task after readback.'
    $relocationRecoveryNames = @(Get-ChildItem `
        -LiteralPath $relocation.RelocationRecoveryRoot `
        -Force | ForEach-Object Name | Sort-Object)
    Assert-True (
        ($relocationRecoveryNames -join "`n") -ceq
            ((@(
                'launcher-backup.ps1',
                'managed-task.test.json',
                'recovery.json',
                'runtime-config-backup.json'
            ) | Sort-Object) -join "`n")
    ) 'Relocation recovery is not bounded to config, launcher, task, and manifest.'
    $relocationRecoveryManifest = Get-Content `
        -LiteralPath (Join-Path $relocation.RelocationRecoveryRoot 'recovery.json') `
        -Raw | ConvertFrom-Json
    Assert-True (
        [string]$relocationRecoveryManifest.schema -ceq
            'sq.librehw.data-root-relocation-recovery.v1'
    ) 'Relocation recovery manifest schema is wrong.'
    Assert-True (
        (Get-TreeSignature -Root $relocation.PreStableRecoveryRoot) -ceq
            $preStableSignature
    ) 'Relocation rewrote the existing pre-stable recovery packet.'
    Assert-True (
        (Get-LhmFileSha256 -Path $relocation.ShimPath) -ceq $relocationShimHash
    ) 'Relocation changed the public shim bytes.'
    Assert-True (
        -not (Test-Path -LiteralPath $relocation.SourceDataRoot) -and
        (Test-Path `
            -LiteralPath (Join-Path $relocation.DataRoot 'logs\sentinel.csv') `
            -PathType Leaf)
    ) 'Relocation copied, recreated, or removed a mutable-data payload.'

    $relocationRecoverySignature =
        Get-TreeSignature -Root $relocation.RelocationRecoveryRoot
    $relocationStateSignature = @(
        Get-LhmFileSha256 -Path (Join-Path $relocation.InstallRoot $script:LhmRuntimeConfigName)
        Get-LhmFileSha256 -Path $relocation.LauncherTarget
        Get-LhmFileSha256 -Path (Join-Path $relocation.ExternalRoot 'managed-task.json')
        Get-LhmFileSha256 -Path $relocation.ShimPath
    ) -join "`n"
    $windowsPowerShellForRelocation =
        Get-Command powershell.exe -CommandType Application -ErrorAction Stop
    $relocationIdempotenceCommand =
        "& '$($relocationScript.Replace("'", "''"))' " +
        "-InstallRoot '$($relocation.InstallRoot.Replace("'", "''"))' " +
        "-SourceDataRoot '$($relocation.SourceDataRoot.Replace("'", "''"))' " +
        "-DataRoot '$($relocation.DataRoot.Replace("'", "''"))' " +
        "-LauncherTargetPath '$($relocation.LauncherTarget.Replace("'", "''"))' " +
        "-PublicShimPath '$($relocation.ShimPath.Replace("'", "''"))' " +
        "-TestExternalStateRoot '$($relocation.ExternalRoot.Replace("'", "''"))' " +
        '-DataMoveAlreadyCompleted -NonLiveTestMode -Confirm:$false'
    $relocationIdempotenceOutput = @(
        & $windowsPowerShellForRelocation.Source `
            -NoLogo `
            -NoProfile `
            -ExecutionPolicy Bypass `
            -Command $relocationIdempotenceCommand 2>&1
    )
    $relocationIdempotenceExitCode = $LASTEXITCODE
    Assert-True (
        $relocationIdempotenceExitCode -eq 0
    ) (
        'Windows PowerShell 5.1 relocation/idempotence run failed: ' +
        ($relocationIdempotenceOutput -join "`n")
    )
    Assert-True (
        (Get-TreeSignature -Root $relocation.RelocationRecoveryRoot) -ceq
            $relocationRecoverySignature -and
        (@(
            Get-LhmFileSha256 -Path (Join-Path $relocation.InstallRoot $script:LhmRuntimeConfigName)
            Get-LhmFileSha256 -Path $relocation.LauncherTarget
            Get-LhmFileSha256 -Path (Join-Path $relocation.ExternalRoot 'managed-task.json')
            Get-LhmFileSha256 -Path $relocation.ShimPath
        ) -join "`n") -ceq $relocationStateSignature
    ) 'Idempotent relocation rewrote recovery or converged state.'

    [System.IO.Directory]::CreateDirectory($relocation.SourceDataRoot) | Out-Null
    Assert-Throws -MessagePattern 'source data root must be absent' -Action {
        $null = & $relocationScript `
            -InstallRoot $relocation.InstallRoot `
            -SourceDataRoot $relocation.SourceDataRoot `
            -DataRoot $relocation.DataRoot `
            -LauncherTargetPath $relocation.LauncherTarget `
            -PublicShimPath $relocation.ShimPath `
            -TestExternalStateRoot $relocation.ExternalRoot `
            -DataMoveAlreadyCompleted `
            -NonLiveTestMode `
            -Confirm:$false
    }

    $reparseRelocation = New-DataRootRelocationFixture `
        -Root $testRoot `
        -Name 'reparse' `
        -CandidateDirectory $candidate1
    $reparseTarget = Join-Path $reparseRelocation.Root 'redirected-data'
    Move-Item -LiteralPath $reparseRelocation.DataRoot -Destination $reparseTarget
    $null = New-Item `
        -ItemType Junction `
        -Path $reparseRelocation.DataRoot `
        -Target $reparseTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            $null = & $relocationScript `
                -InstallRoot $reparseRelocation.InstallRoot `
                -SourceDataRoot $reparseRelocation.SourceDataRoot `
                -DataRoot $reparseRelocation.DataRoot `
                -LauncherTargetPath $reparseRelocation.LauncherTarget `
                -PublicShimPath $reparseRelocation.ShimPath `
                -TestExternalStateRoot $reparseRelocation.ExternalRoot `
                -DataMoveAlreadyCompleted `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            Test-Path -LiteralPath (Join-Path $reparseTarget 'logs\sentinel.csv') -PathType Leaf
        ) 'Relocation traversed a reparse-point data root.'
    }
    finally {
        if ([System.IO.Directory]::Exists($reparseRelocation.DataRoot)) {
            [System.IO.Directory]::Delete($reparseRelocation.DataRoot, $false)
        }
    }

    $failedRelocation = New-DataRootRelocationFixture `
        -Root $testRoot `
        -Name 'failure-resume' `
        -CandidateDirectory $candidate1
    $failedShimHash = Get-LhmFileSha256 -Path $failedRelocation.ShimPath
    $failedPreStableSignature =
        Get-TreeSignature -Root $failedRelocation.PreStableRecoveryRoot
    Assert-Throws -MessagePattern 'AfterTaskEnabled' -Action {
        $null = & $relocationScript `
            -InstallRoot $failedRelocation.InstallRoot `
            -SourceDataRoot $failedRelocation.SourceDataRoot `
            -DataRoot $failedRelocation.DataRoot `
            -LauncherTargetPath $failedRelocation.LauncherTarget `
            -PublicShimPath $failedRelocation.ShimPath `
            -TestExternalStateRoot $failedRelocation.ExternalRoot `
            -DataMoveAlreadyCompleted `
            -NonLiveTestMode `
            -TestFailurePoint AfterTaskEnabled `
            -Confirm:$false
    }
    $failedTask = Get-Content `
        -LiteralPath (Join-Path $failedRelocation.ExternalRoot 'managed-task.json') `
        -Raw | ConvertFrom-Json
    Assert-True (-not [bool]$failedTask.enabled) `
        'Pre-activation relocation failure did not leave the managed task disabled.'
    Assert-True (
        Test-Path -LiteralPath $failedRelocation.RelocationRecoveryRoot -PathType Container
    ) 'Pre-activation relocation failure did not retain recovery evidence.'
    Assert-True (
        (Get-LhmFileSha256 -Path $failedRelocation.ShimPath) -ceq $failedShimHash -and
        (Get-TreeSignature -Root $failedRelocation.PreStableRecoveryRoot) -ceq
            $failedPreStableSignature
    ) 'Pre-activation failure changed the shim or pre-stable recovery packet.'
    $failedRecoverySignature =
        Get-TreeSignature -Root $failedRelocation.RelocationRecoveryRoot
    $null = & $relocationScript `
        -InstallRoot $failedRelocation.InstallRoot `
        -SourceDataRoot $failedRelocation.SourceDataRoot `
        -DataRoot $failedRelocation.DataRoot `
        -LauncherTargetPath $failedRelocation.LauncherTarget `
        -PublicShimPath $failedRelocation.ShimPath `
        -TestExternalStateRoot $failedRelocation.ExternalRoot `
        -DataMoveAlreadyCompleted `
        -NonLiveTestMode `
        -Confirm:$false
    Assert-True (
        (Get-TreeSignature -Root $failedRelocation.RelocationRecoveryRoot) -ceq
            $failedRecoverySignature
    ) 'Resumed relocation rewrote its original recovery evidence.'
    $resumedTask = Get-Content `
        -LiteralPath (Join-Path $failedRelocation.ExternalRoot 'managed-task.json') `
        -Raw | ConvertFrom-Json
    Assert-True ([bool]$resumedTask.enabled) `
        'Resumed relocation did not converge the existing managed task.'

    Add-Content `
        -LiteralPath (Join-Path $failedRelocation.RelocationRecoveryRoot 'launcher-backup.ps1') `
        -Value '# tampered'
    Assert-Throws -MessagePattern 'hash' -Action {
        $null = & $relocationScript `
            -InstallRoot $failedRelocation.InstallRoot `
            -SourceDataRoot $failedRelocation.SourceDataRoot `
            -DataRoot $failedRelocation.DataRoot `
            -LauncherTargetPath $failedRelocation.LauncherTarget `
            -PublicShimPath $failedRelocation.ShimPath `
            -TestExternalStateRoot $failedRelocation.ExternalRoot `
            -DataMoveAlreadyCompleted `
            -NonLiveTestMode `
            -Confirm:$false
    }

    $allScripts = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File)
    foreach ($scriptFile in $allScripts) {
        $tokens = $null
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            $scriptFile.FullName,
            [ref]$tokens,
            [ref]$errors)
        Assert-True ($errors.Count -eq 0) "PowerShell parser errors in '$($scriptFile.Name)'."
    }

    $identityFixtureRoot = Join-Path $testRoot 'identity-verifiers'
    [System.IO.Directory]::CreateDirectory($identityFixtureRoot) | Out-Null
    $originalIdentityVerifierPath = $script:LhmIdentityVerifierPath
    $originalExpectedMachineId = $script:LhmExpectedMachineId
    $originalExpectedInstanceId = $script:LhmExpectedInstanceId
    $missingIdentityVerifier = Join-Path $identityFixtureRoot 'missing.ps1'
    try {
        $script:LhmExpectedMachineId = 'snd-desk'
        $script:LhmExpectedInstanceId = 'ca96d510-7d87-4cec-8e1a-bd8fc3866903'
        $script:LhmIdentityVerifierPath = $missingIdentityVerifier
        Assert-Throws -MessagePattern 'Machine identity verifier not found' -Action {
            $null = Assert-LhmVerifiedMachineIdentity
        }

        $noResultIdentityVerifier = New-TestIdentityVerifier `
            -Root $identityFixtureRoot `
            -Name 'no-result' `
            -NoResult
        $script:LhmIdentityVerifierPath = $noResultIdentityVerifier
        Assert-Throws -MessagePattern 'must return exactly one result' -Action {
            $null = Assert-LhmVerifiedMachineIdentity
        }

        $duplicateIdentityVerifier = New-TestIdentityVerifier `
            -Root $identityFixtureRoot `
            -Name 'duplicate-result' `
            -Status 'VERIFIED' `
            -MachineId 'snd-desk' `
            -DuplicateResult
        $script:LhmIdentityVerifierPath = $duplicateIdentityVerifier
        Assert-Throws -MessagePattern 'must return exactly one result' -Action {
            $null = Assert-LhmVerifiedMachineIdentity
        }

        $unverifiedIdentityVerifier = New-TestIdentityVerifier `
            -Root $identityFixtureRoot `
            -Name 'unverified' `
            -Status 'UNVERIFIED' `
            -MachineId 'snd-desk'
        $script:LhmIdentityVerifierPath = $unverifiedIdentityVerifier
        Assert-Throws -MessagePattern "status is 'UNVERIFIED', not 'VERIFIED'" -Action {
            $null = Assert-LhmVerifiedMachineIdentity
        }

        $wrongMachineIdentityVerifier = New-TestIdentityVerifier `
            -Root $identityFixtureRoot `
            -Name 'wrong-machine' `
            -Status 'VERIFIED' `
            -MachineId 'snd-host'
        $script:LhmIdentityVerifierPath = $wrongMachineIdentityVerifier
        Assert-Throws -MessagePattern "identity is 'snd-host', not 'snd-desk'" -Action {
            $null = Assert-LhmVerifiedMachineIdentity
        }

        $wrongInstanceIdentityVerifier = New-TestIdentityVerifier `
            -Root $identityFixtureRoot `
            -Name 'wrong-instance' `
            -Status 'VERIFIED' `
            -MachineId 'snd-desk' `
            -InstanceId '11111111-1111-1111-1111-111111111111'
        $script:LhmIdentityVerifierPath = $wrongInstanceIdentityVerifier
        Assert-Throws -MessagePattern "installation identity is '11111111-1111-1111-1111-111111111111'" -Action {
            $null = Assert-LhmVerifiedMachineIdentity
        }

        $verifiedIdentityVerifier = New-TestIdentityVerifier `
            -Root $identityFixtureRoot `
            -Name 'verified-snd-desk' `
            -Status 'VERIFIED' `
            -MachineId 'snd-desk'
        $script:LhmIdentityVerifierPath = $verifiedIdentityVerifier
        $verifiedIdentity = Assert-LhmVerifiedMachineIdentity
        Assert-True (
            [string]$verifiedIdentity.status -ceq 'VERIFIED' -and
            [string]$verifiedIdentity.machineId -ceq 'snd-desk' -and
            [string]$verifiedIdentity.instanceId -ceq
                'ca96d510-7d87-4cec-8e1a-bd8fc3866903'
        ) 'The valid SND-DESK identity result was not returned unchanged.'
    }
    finally {
        $script:LhmIdentityVerifierPath = $originalIdentityVerifierPath
        $script:LhmExpectedMachineId = $originalExpectedMachineId
        $script:LhmExpectedInstanceId = $originalExpectedInstanceId
    }

    $launcherMissingIdentity = Invoke-TestLauncherIdentity `
        -LauncherPath $canonicalLauncher `
        -VerifierPath $missingIdentityVerifier
    Assert-True (
        $launcherMissingIdentity.ExitCode -ne 0 -and
        $launcherMissingIdentity.Output -match 'Machine identity verifier not found'
    ) 'The launcher identity gate accepted a missing verifier.'

    $launcherDuplicateIdentity = Invoke-TestLauncherIdentity `
        -LauncherPath $canonicalLauncher `
        -VerifierPath $duplicateIdentityVerifier
    Assert-True (
        $launcherDuplicateIdentity.ExitCode -ne 0 -and
        $launcherDuplicateIdentity.Output -match 'must return exactly one result'
    ) 'The launcher identity gate accepted multiple verifier results.'

    $launcherUnverifiedIdentity = Invoke-TestLauncherIdentity `
        -LauncherPath $canonicalLauncher `
        -VerifierPath $unverifiedIdentityVerifier
    Assert-True (
        $launcherUnverifiedIdentity.ExitCode -ne 0 -and
        $launcherUnverifiedIdentity.Output -match 'restricted to verified machine'
    ) 'The launcher identity gate accepted an unverified identity.'

    $launcherWrongMachineIdentity = Invoke-TestLauncherIdentity `
        -LauncherPath $canonicalLauncher `
        -VerifierPath $wrongMachineIdentityVerifier
    Assert-True (
        $launcherWrongMachineIdentity.ExitCode -ne 0 -and
        $launcherWrongMachineIdentity.Output -match 'restricted to verified machine'
    ) 'The launcher identity gate accepted a different machine.'

    $launcherWrongInstanceIdentity = Invoke-TestLauncherIdentity `
        -LauncherPath $canonicalLauncher `
        -VerifierPath $wrongInstanceIdentityVerifier
    Assert-True (
        $launcherWrongInstanceIdentity.ExitCode -ne 0 -and
        $launcherWrongInstanceIdentity.Output -match 'restricted to verified machine'
    ) 'The launcher identity gate accepted a different installation.'

    $launcherVerifiedIdentity = Invoke-TestLauncherIdentity `
        -LauncherPath $canonicalLauncher `
        -VerifierPath $verifiedIdentityVerifier
    Assert-True ($launcherVerifiedIdentity.ExitCode -eq 0) (
        'The launcher identity gate rejected VERIFIED/snd-desk: ' +
        $launcherVerifiedIdentity.Output
    )

    $hashWithoutWhatIf = Get-LhmFileSha256 -Path $canonicalLauncher
    $savedWhatIfPreference = $WhatIfPreference
    try {
        $WhatIfPreference = $true
        $hashWithWhatIf = Get-LhmFileSha256 -Path $canonicalLauncher
    }
    finally {
        $WhatIfPreference = $savedWhatIfPreference
    }
    Assert-True ($hashWithWhatIf -ceq $hashWithoutWhatIf) `
        'Read-only SHA-256 preflight did not survive inherited WhatIfPreference.'

    $null = Assert-LhmRelocationHealthUri `
        -HealthUri ([uri]$script:LhmProductionHealthUri)
    Assert-Throws -MessagePattern 'Production health URI must be' -Action {
        $null = Assert-LhmRelocationHealthUri `
            -HealthUri ([uri]'http://localhost:18085/data.json')
    }
    $null = Assert-LhmRelocationHealthUri `
        -HealthUri ([uri]'http://localhost:18085/data.json') `
        -IsTest

    $productionTaskExecutable =
        'E:\SQ_HQ\Monitoring\LibreHW\LibreHardwareMonitor.Windows.Forms.exe'
    $productionTaskInstallRoot = 'E:\SQ_HQ\Monitoring\LibreHW'
    $productionTask = New-ProductionRelocationTaskFixture `
        -ExecutablePath $productionTaskExecutable `
        -InstallRoot $productionTaskInstallRoot
    $null = Assert-LhmRelocationTaskContract `
        -Task $productionTask `
        -ManagedTaskPath $script:LhmManagedTaskPath `
        -ExpectedExecutablePath $productionTaskExecutable `
        -ExpectedInstallRoot $productionTaskInstallRoot `
        -RequireDisabled

    $foreignArgumentsTask = New-ProductionRelocationTaskFixture `
        -ExecutablePath $productionTaskExecutable `
        -InstallRoot $productionTaskInstallRoot
    $foreignArgumentsTask.Actions[0].Arguments = '--foreign'
    Assert-Throws -MessagePattern 'exact relocation contract' -Action {
        $null = Assert-LhmRelocationTaskContract `
            -Task $foreignArgumentsTask `
            -ManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedExecutablePath $productionTaskExecutable `
            -ExpectedInstallRoot $productionTaskInstallRoot
    }

    $foreignPrincipalTask = New-ProductionRelocationTaskFixture `
        -ExecutablePath $productionTaskExecutable `
        -InstallRoot $productionTaskInstallRoot
    $foreignPrincipalTask.Principal.UserId = 'OtherUser'
    Assert-Throws -MessagePattern 'exact relocation contract' -Action {
        $null = Assert-LhmRelocationTaskContract `
            -Task $foreignPrincipalTask `
            -ManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedExecutablePath $productionTaskExecutable `
            -ExpectedInstallRoot $productionTaskInstallRoot
    }

    $foreignTriggerTask = New-ProductionRelocationTaskFixture `
        -ExecutablePath $productionTaskExecutable `
        -InstallRoot $productionTaskInstallRoot
    $foreignTriggerTask.Triggers[0].UserId = 'SND-HOST\Sev'
    Assert-Throws -MessagePattern 'exact relocation contract' -Action {
        $null = Assert-LhmRelocationTaskContract `
            -Task $foreignTriggerTask `
            -ManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedExecutablePath $productionTaskExecutable `
            -ExpectedInstallRoot $productionTaskInstallRoot
    }

    $absentFilesystemDriveLetter = $null
    foreach ($driveLetter in [char[]](90..65)) {
        $driveName = [string]$driveLetter
        if ($null -eq (Get-PSDrive -Name $driveName -ErrorAction SilentlyContinue) -and
            -not [System.IO.Directory]::Exists("$driveName`:\")) {
            $absentFilesystemDriveLetter = $driveName
            break
        }
    }
    Assert-True (
        -not [string]::IsNullOrWhiteSpace($absentFilesystemDriveLetter)
    ) 'No absent filesystem drive was available for lexical path validation.'
    $absentFilesystemPath =
        "$absentFilesystemDriveLetter`:\SQ_HQ\Monitoring\staging\..\LibreHW\librehw.runtime.json"
    $expectedAbsentFilesystemPath =
        "$absentFilesystemDriveLetter`:\SQ_HQ\Monitoring\LibreHW\librehw.runtime.json"
    Assert-True (
        (Resolve-LhmFullPath -Path $absentFilesystemPath) -ceq
            $expectedAbsentFilesystemPath
    ) 'Absolute filesystem path normalization required its drive to exist.'
    $absentFilesystemRoot = "$absentFilesystemDriveLetter`:\"
    Assert-True (
        (Resolve-LhmFullPath -Path $absentFilesystemRoot) -ceq
            $absentFilesystemRoot
    ) 'Absolute filesystem root normalization removed its root separator.'
    Assert-True (
        Test-LhmPathWithin `
            -Path ($absentFilesystemRoot + 'nested\payload.txt') `
            -Root $absentFilesystemRoot
    ) 'Filesystem root containment added a second root separator.'
    Assert-True (
        -not (Test-LhmPathWithin `
            -Path $absentFilesystemRoot `
            -Root $absentFilesystemRoot)
    ) 'Filesystem root containment must remain strictly below the root.'
    $existingFilesystemRoot = [System.IO.Path]::GetPathRoot(
        (Resolve-LhmFullPath -Path $testRoot))
    Assert-True (
        Test-LhmPathWithin -Path $testRoot -Root $existingFilesystemRoot
    ) 'Existing filesystem root containment added a second root separator.'
    Assert-True (
        -not (Test-LhmPathWithin `
            -Path $existingFilesystemRoot `
            -Root $existingFilesystemRoot)
    ) 'Existing filesystem root containment must remain strictly below the root.'

    $mappedDriveRoot = Join-Path $testRoot 'mapped-drive-root'
    [System.IO.Directory]::CreateDirectory($mappedDriveRoot) | Out-Null
    $null = New-PSDrive `
        -Name $absentFilesystemDriveLetter `
        -PSProvider FileSystem `
        -Root $mappedDriveRoot `
        -Scope Script
    try {
        $mappedDrivePath = "$absentFilesystemDriveLetter`:\child\..\payload"
        Assert-True (
            (Resolve-LhmFullPath -Path $mappedDrivePath) -ceq
                (Join-Path $mappedDriveRoot 'payload')
        ) 'Existing filesystem PSDrive mapping was bypassed during path normalization.'
    }
    finally {
        Remove-PSDrive -Name $absentFilesystemDriveLetter -Scope Script
    }

    $currentLauncherValidation = & $canonicalLauncher -ValidateScriptOnly
    Assert-True (
        $currentLauncherValidation.Result -ceq 'PASS' -and
        $currentLauncherValidation.DataRoot -ceq '%SEV_LOCAL_DATA%\LibreHardwareMonitor' -and
        $currentLauncherValidation.ExpectedInstanceId -ceq
            'ca96d510-7d87-4cec-8e1a-bd8fc3866903' -and
        -not [bool]$currentLauncherValidation.MutationPerformed
    ) 'PowerShell 7 launcher validation did not report the dedicated data root.'

    $windowsPowerShell = Get-Command powershell.exe -CommandType Application -ErrorAction Stop
    $launcherCompatibilityHarness = @"
function Get-Process {
    [CmdletBinding()]
    param([string] `$Name)
    return @()
}

. '$canonicalLauncher' -ValidateScriptOnly | Out-Null
Initialize-LauncherNativeMethods
"@
    $launcherCompatibilityOutput = @(
        & $windowsPowerShell.Source `
            -NoLogo `
            -NoProfile `
            -ExecutionPolicy Bypass `
            -Command $launcherCompatibilityHarness 2>&1
    )
    $launcherCompatibilityExitCode = $LASTEXITCODE
    Assert-True (
        $launcherCompatibilityExitCode -eq 0
    ) (
        'Windows PowerShell 5.1 launcher/native compilation compatibility failed: ' +
        ($launcherCompatibilityOutput -join "`n")
    )

    $launcherNoProcessHarness = @"
function Get-Process {
    [CmdletBinding()]
    param([string] `$Name)
    return @()
}

& '$canonicalLauncher' -ValidateScriptOnly
"@
    $launcherNoProcessOutput = @(
        & $windowsPowerShell.Source `
            -NoLogo `
            -NoProfile `
            -ExecutionPolicy Bypass `
            -Command $launcherNoProcessHarness 2>&1
    )
    $launcherNoProcessExitCode = $LASTEXITCODE
    Assert-True (
        $launcherNoProcessExitCode -eq 0
    ) (
        'Windows PowerShell 5.1 empty-process launcher compatibility failed: ' +
        ($launcherNoProcessOutput -join "`n")
    )
    Assert-True (
        ($launcherNoProcessOutput -join "`n") -match
            'DetectedProcessCount\s*:\s*0' -and
        ($launcherNoProcessOutput -join "`n") -match
            'DataRoot\s*:\s*%SEV_LOCAL_DATA%\\LibreHardwareMonitor'
    ) (
        'Windows PowerShell 5.1 empty-process launcher validation did not ' +
        'report zero detected processes and the dedicated data root.'
    )

    $launcherMismatchedProcessHarness = @"
function Get-Process {
    [CmdletBinding()]
    param([string] `$Name)
    return @([pscustomobject]@{
        Id = 424242
        Path = 'C:\OtherMachine\LibreHardwareMonitor.Windows.Forms.exe'
    })
}

& '$canonicalLauncher' -ValidateScriptOnly
"@
    $launcherMismatchedProcessOutput = @(
        & $windowsPowerShell.Source `
            -NoLogo `
            -NoProfile `
            -ExecutionPolicy Bypass `
            -Command $launcherMismatchedProcessHarness 2>&1
    )
    $launcherMismatchedProcessExitCode = $LASTEXITCODE
    Assert-True (
        $launcherMismatchedProcessExitCode -eq 0
    ) (
        'Windows PowerShell 5.1 path-mismatched source validation failed: ' +
        ($launcherMismatchedProcessOutput -join "`n")
    )
    Assert-True (
        ($launcherMismatchedProcessOutput -join "`n") -match
            'DetectedProcessCount\s*:\s*1'
    ) (
        'Windows PowerShell 5.1 path-mismatched source validation did not ' +
        'report the detected process.'
    )

    $emptyOutputCleanupFixtureRoot =
        Join-Path $testRoot 'cleanup-empty-output-repository'
    [System.IO.Directory]::CreateDirectory(
        (Join-Path $emptyOutputCleanupFixtureRoot 'Aga.Controls\bin')) | Out-Null
    foreach ($marker in @(
        'LibreHardwareMonitor.sln',
        'Directory.Build.props',
        'Directory.Packages.props'
    )) {
        'cleanup test marker' |
            Set-Content `
                -LiteralPath (Join-Path $emptyOutputCleanupFixtureRoot $marker) `
                -Encoding UTF8
    }
    $emptyOutputCleanupOutput = @(
        & $windowsPowerShell.Source `
            -NoLogo `
            -NoProfile `
            -ExecutionPolicy Bypass `
            -File $cleanupScript `
            -RepositoryRoot $emptyOutputCleanupFixtureRoot `
            -WhatIf 2>&1
    )
    $emptyOutputCleanupExitCode = $LASTEXITCODE
    Assert-True (
        $emptyOutputCleanupExitCode -eq 0
    ) (
        'Windows PowerShell 5.1 cleanup of an empty allowed output directory failed: ' +
        ($emptyOutputCleanupOutput -join "`n")
    )

    $cleanupCompatibilityOutput = @(
        & $windowsPowerShell.Source `
            -NoLogo `
            -NoProfile `
            -ExecutionPolicy Bypass `
            -File $cleanupScript `
            -WhatIf 2>&1
    )
    $cleanupCompatibilityExitCode = $LASTEXITCODE
    Assert-True (
        $cleanupCompatibilityExitCode -eq 0
    ) (
        'Windows PowerShell 5.1 default cleanup invocation failed: ' +
        ($cleanupCompatibilityOutput -join "`n")
    )

    $cleanupFixtureRoot = Join-Path $testRoot 'cleanup-repository'
    $cleanupOutsideRoot = Join-Path $testRoot 'cleanup-outside'
    $junctionPath = Join-Path $cleanupFixtureRoot 'Aga.Controls'
    $outsideBin = Join-Path $cleanupOutsideRoot 'bin'
    $outsideSentinel = Join-Path $outsideBin 'must-not-delete.txt'
    [System.IO.Directory]::CreateDirectory($cleanupFixtureRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($outsideBin) | Out-Null
    foreach ($marker in @(
        'LibreHardwareMonitor.sln',
        'Directory.Build.props',
        'Directory.Packages.props'
    )) {
        'cleanup test marker' |
            Set-Content `
                -LiteralPath (Join-Path $cleanupFixtureRoot $marker) `
                -Encoding UTF8
    }
    'outside fixture' |
        Set-Content -LiteralPath $outsideSentinel -Encoding UTF8
    $null = New-Item `
        -ItemType Junction `
        -Path $junctionPath `
        -Target $cleanupOutsideRoot
    try {
        Assert-Throws -MessagePattern 'ancestry contains a reparse point' -Action {
            & $cleanupScript `
                -RepositoryRoot $cleanupFixtureRoot `
                -Confirm:$false
        }
        Assert-True (
            Test-Path -LiteralPath $outsideSentinel -PathType Leaf
        ) 'Cleanup traversed a junction parent and deleted external data.'
    }
    finally {
        if ([System.IO.Directory]::Exists($junctionPath)) {
            [System.IO.Directory]::Delete($junctionPath, $false)
        }
    }

    $nestedOutputRoot = Join-Path $cleanupFixtureRoot 'Aga.Controls\bin'
    $nestedJunctionPath = Join-Path $nestedOutputRoot 'external-link'
    [System.IO.Directory]::CreateDirectory($nestedOutputRoot) | Out-Null
    $null = New-Item `
        -ItemType Junction `
        -Path $nestedJunctionPath `
        -Target $outsideBin
    try {
        $nestedCleanupCommand = (
            "& '$($cleanupScript.Replace("'", "''"))' " +
            "-RepositoryRoot '$($cleanupFixtureRoot.Replace("'", "''"))' " +
            '-Confirm:$false'
        )
        $savedErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $nestedCleanupOutput = @(
                & $windowsPowerShell.Source `
                    -NoLogo `
                    -NoProfile `
                    -ExecutionPolicy Bypass `
                    -Command $nestedCleanupCommand 2>&1
            )
            $nestedCleanupExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $savedErrorActionPreference
        }
        Assert-True (
            $nestedCleanupExitCode -ne 0 -and
            ($nestedCleanupOutput -join "`n") -match 'contains a reparse point'
        ) (
            'Windows PowerShell 5.1 nested-junction cleanup guard failed: ' +
            ($nestedCleanupOutput -join "`n")
        )
        Assert-True (
            Test-Path -LiteralPath $outsideSentinel -PathType Leaf
        ) 'Cleanup traversed a nested junction and deleted external data.'
    }
    finally {
        if ([System.IO.Directory]::Exists($nestedJunctionPath)) {
            [System.IO.Directory]::Delete($nestedJunctionPath, $false)
        }
    }

    Assert-Throws -MessagePattern 'must not be a filesystem root' -Action {
        & $cleanupScript `
            -RepositoryRoot ([System.IO.Path]::GetPathRoot($cleanupFixtureRoot)) `
            -WhatIf
    }

    $preStableTaskContractPath = Join-Path $testRoot 'pre-stable-task-contract.xml'
    $preStableUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $preStableWorkingDirectory =
        Split-Path -Parent $script:LhmPreStableManagedExecutablePath
    @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><URI>$($script:LhmManagedTaskPath)</URI></RegistrationInfo>
  <Principals><Principal id="Author"><UserId>$preStableUserSid</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings><AllowHardTerminate>false</AllowHardTerminate><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><StartWhenAvailable>true</StartWhenAvailable></Settings>
  <Triggers />
  <Actions Context="Author"><Exec><Command>$($script:LhmPreStableManagedExecutablePath)</Command><WorkingDirectory>$preStableWorkingDirectory</WorkingDirectory></Exec></Actions>
</Task>
"@ | Set-Content -LiteralPath $preStableTaskContractPath -Encoding Unicode
    Assert-LhmPreStableManagedTaskBackupContract -Path $preStableTaskContractPath

    $hostilePreStableTaskContractPath =
        Join-Path $testRoot 'pre-stable-task-contract-hostile.xml'
    (Get-Content -LiteralPath $preStableTaskContractPath -Raw -Encoding Unicode).Replace(
        $script:LhmPreStableManagedExecutablePath,
        'C:\Windows\System32\cmd.exe') |
        Set-Content -LiteralPath $hostilePreStableTaskContractPath -Encoding Unicode
    Assert-Throws -MessagePattern 'exact discovered task contract' -Action {
        Assert-LhmPreStableManagedTaskBackupContract `
            -Path $hostilePreStableTaskContractPath
    }

    $legacyFixtureRoot = New-LegacyRecoveryFixture `
        -Root (Join-Path $testRoot 'legacy-recovery-valid') `
        -PublicShimPath $shimPath `
        -PublicShimSha256 $shimHash
    $null = Read-LhmLegacyRecoveryPacket `
        -RecoveryRoot $legacyFixtureRoot `
        -ExpectedPublicShimPath $shimPath `
        -ExpectedPublicShimSha256 $shimHash `
        -NonLiveTestMode

    $absentShortcutRoot = Join-Path $testRoot 'legacy-recovery-absent-shortcuts'
    Copy-Item -LiteralPath $legacyFixtureRoot -Destination $absentShortcutRoot -Recurse
    $absentShortcutManifestPath = Join-Path $absentShortcutRoot 'recovery.json'
    $absentShortcutManifest =
        Get-Content -LiteralPath $absentShortcutManifestPath -Raw |
        ConvertFrom-Json
    foreach ($record in $absentShortcutManifest.shortcuts) {
        $record.existed = $false
        $record.backup = $null
        $record.sha256 = $null
    }
    $absentShortcutManifest | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath $absentShortcutManifestPath
    Remove-Item -LiteralPath (Join-Path $absentShortcutRoot '0.lnk') -Force
    Remove-Item -LiteralPath (Join-Path $absentShortcutRoot '1.lnk') -Force
    $null = Read-LhmLegacyRecoveryPacket `
        -RecoveryRoot $absentShortcutRoot `
        -ExpectedPublicShimPath $shimPath `
        -ExpectedPublicShimSha256 $shimHash

    $tamperedPathRoot = Join-Path $testRoot 'legacy-recovery-tampered-path'
    Copy-Item -LiteralPath $legacyFixtureRoot -Destination $tamperedPathRoot -Recurse
    $tamperedPathManifest =
        Get-Content -LiteralPath (Join-Path $tamperedPathRoot 'recovery.json') -Raw |
        ConvertFrom-Json
    $tamperedPathManifest.shortcuts[0].path = 'C:\Windows\System32\arbitrary.lnk'
    $tamperedPathManifest | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $tamperedPathRoot 'recovery.json')
    Assert-Throws -MessagePattern 'shortcut record 0' -Action {
        $null = Read-LhmLegacyRecoveryPacket `
            -RecoveryRoot $tamperedPathRoot `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -NonLiveTestMode
    }

    $tamperedTypeRoot = Join-Path $testRoot 'legacy-recovery-tampered-type'
    Copy-Item -LiteralPath $legacyFixtureRoot -Destination $tamperedTypeRoot -Recurse
    $tamperedTypeManifest =
        Get-Content -LiteralPath (Join-Path $tamperedTypeRoot 'recovery.json') -Raw |
        ConvertFrom-Json
    $tamperedTypeManifest.shortcuts[0].existed = 'true'
    $tamperedTypeManifest | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $tamperedTypeRoot 'recovery.json')
    Assert-Throws -MessagePattern 'shortcut record 0' -Action {
        $null = Read-LhmLegacyRecoveryPacket `
            -RecoveryRoot $tamperedTypeRoot `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -NonLiveTestMode
    }

    $tamperedXmlRoot = Join-Path $testRoot 'legacy-recovery-tampered-xml'
    Copy-Item -LiteralPath $legacyFixtureRoot -Destination $tamperedXmlRoot -Recurse
    $tamperedXmlPath = Join-Path $tamperedXmlRoot 'legacy-task.xml'
    $tamperedXmlText =
        Get-Content -LiteralPath $tamperedXmlPath -Raw -Encoding Unicode
    $tamperedXmlText.Replace(
        $script:LhmLegacyRootTaskExecutablePath,
        'C:\Windows\System32\cmd.exe') |
        Set-Content -LiteralPath $tamperedXmlPath -Encoding Unicode
    $tamperedXmlManifest =
        Get-Content -LiteralPath (Join-Path $tamperedXmlRoot 'recovery.json') -Raw |
        ConvertFrom-Json
    $tamperedXmlManifest.legacyTaskXmlSha256 = Get-LhmFileSha256 -Path $tamperedXmlPath
    $tamperedXmlManifest | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $tamperedXmlRoot 'recovery.json')
    Assert-Throws -MessagePattern 'exact discovered legacy task contract' -Action {
        $null = Read-LhmLegacyRecoveryPacket `
            -RecoveryRoot $tamperedXmlRoot `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -NonLiveTestMode
    }

    $unexpectedEntryRoot = Join-Path $testRoot 'legacy-recovery-unexpected-entry'
    Copy-Item -LiteralPath $legacyFixtureRoot -Destination $unexpectedEntryRoot -Recurse
    'unexpected' | Set-Content -LiteralPath (Join-Path $unexpectedEntryRoot 'extra.bin')
    Assert-Throws -MessagePattern 'unexpected or unsafe entries' -Action {
        $null = Read-LhmLegacyRecoveryPacket `
            -RecoveryRoot $unexpectedEntryRoot `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -NonLiveTestMode
    }

    $tamperedShortcutRoot = Join-Path $testRoot 'legacy-recovery-tampered-shortcut'
    Copy-Item -LiteralPath $legacyFixtureRoot -Destination $tamperedShortcutRoot -Recurse
    $tamperedShortcutPath = Join-Path $tamperedShortcutRoot '0.lnk'
    $shell = $null
    $shortcut = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($tamperedShortcutPath)
        $shortcut.TargetPath = 'C:\Windows\System32\cmd.exe'
        $shortcut.Arguments = '/c exit'
        $shortcut.WorkingDirectory = 'C:\Windows\System32'
        $shortcut.Save()
    }
    finally {
        if ($null -ne $shortcut -and
            [System.Runtime.InteropServices.Marshal]::IsComObject($shortcut)) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        }
        if ($null -ne $shell -and
            [System.Runtime.InteropServices.Marshal]::IsComObject($shell)) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
    }
    $tamperedShortcutManifest =
        Get-Content -LiteralPath (Join-Path $tamperedShortcutRoot 'recovery.json') -Raw |
        ConvertFrom-Json
    $tamperedShortcutManifest.shortcuts[0].sha256 =
        Get-LhmFileSha256 -Path $tamperedShortcutPath
    $tamperedShortcutManifest | ConvertTo-Json -Depth 6 | Set-Content `
        -LiteralPath (Join-Path $tamperedShortcutRoot 'recovery.json')
    Assert-Throws -MessagePattern 'exact discovered target contract' -Action {
        $null = Read-LhmLegacyRecoveryPacket `
            -RecoveryRoot $tamperedShortcutRoot `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -NonLiveTestMode
    }

    $junctionTargetParent = Join-Path $testRoot 'legacy-recovery-junction-target'
    [System.IO.Directory]::CreateDirectory($junctionTargetParent) | Out-Null
    Copy-Item `
        -LiteralPath $legacyFixtureRoot `
        -Destination (Join-Path $junctionTargetParent 'packet') `
        -Recurse
    $junctionParent = Join-Path $testRoot 'legacy-recovery-junction-parent'
    $null = New-Item `
        -ItemType Junction `
        -Path $junctionParent `
        -Target $junctionTargetParent
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            $null = Read-LhmLegacyRecoveryPacket `
                -RecoveryRoot (Join-Path $junctionParent 'packet') `
                -ExpectedPublicShimPath $shimPath `
                -ExpectedPublicShimSha256 $shimHash `
                -NonLiveTestMode
        }
    }
    finally {
        if ([System.IO.Directory]::Exists($junctionParent)) {
            [System.IO.Directory]::Delete($junctionParent, $false)
        }
    }

    $installRootJunctionCase = Join-Path $testRoot 'install-root-junction'
    $installRootJunctionTarget = Join-Path $testRoot 'install-root-junction-target'
    [System.IO.Directory]::CreateDirectory($installRootJunctionCase) | Out-Null
    [System.IO.Directory]::CreateDirectory($installRootJunctionTarget) | Out-Null
    $redirectedInstallRoot = Join-Path $installRootJunctionCase 'install'
    $null = New-Item `
        -ItemType Junction `
        -Path $redirectedInstallRoot `
        -Target $installRootJunctionTarget
    $installRootJunctionTargetSignature =
        Get-TreeSignature -Root $installRootJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot $redirectedInstallRoot `
                -DataRoot (Join-Path $installRootJunctionCase 'data') `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $installRootJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $installRootJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $installRootJunctionTarget) -ceq
                $installRootJunctionTargetSignature
        ) 'Installer wrote through a pre-existing install-root junction.'
    }
    finally {
        if ([System.IO.Directory]::Exists($redirectedInstallRoot)) {
            [System.IO.Directory]::Delete($redirectedInstallRoot, $false)
        }
    }

    $dataRootJunctionCase = Join-Path $testRoot 'data-root-junction'
    $dataRootJunctionTarget = Join-Path $testRoot 'data-root-junction-target'
    [System.IO.Directory]::CreateDirectory($dataRootJunctionCase) | Out-Null
    [System.IO.Directory]::CreateDirectory($dataRootJunctionTarget) | Out-Null
    $redirectedDataRoot = Join-Path $dataRootJunctionCase 'data'
    $null = New-Item `
        -ItemType Junction `
        -Path $redirectedDataRoot `
        -Target $dataRootJunctionTarget
    $dataRootJunctionTargetSignature =
        Get-TreeSignature -Root $dataRootJunctionTarget
    $dataJunctionInstallRoot = Join-Path $dataRootJunctionCase 'install'
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot $dataJunctionInstallRoot `
                -DataRoot $redirectedDataRoot `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $dataRootJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $dataRootJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $dataRootJunctionTarget) -ceq
                $dataRootJunctionTargetSignature
        ) 'Installer wrote through a pre-existing data-root junction.'
        Assert-True (
            -not (Test-Path -LiteralPath $dataJunctionInstallRoot)
        ) 'Data-root preflight created the install root before rejecting the junction.'
    }
    finally {
        if ([System.IO.Directory]::Exists($redirectedDataRoot)) {
            [System.IO.Directory]::Delete($redirectedDataRoot, $false)
        }
    }

    $installJunctionRoot = Join-Path $testRoot 'install-recovery-parent-junction'
    $installJunctionInstallRoot = Join-Path $installJunctionRoot 'install'
    $installJunctionDataRoot = Join-Path $installJunctionRoot 'data'
    $installJunctionExternalRoot = Join-Path $installJunctionRoot 'external'
    $installJunctionLauncher =
        Join-Path $installJunctionRoot 'script-data\Start-LibreHardwareMonitor.ps1'
    $installJunctionShim = Join-Path $installJunctionRoot 'bin\librehw.cmd'
    $installJunctionConfig = Join-Path $installJunctionRoot 'initial.config'
    $installJunctionTarget = Join-Path $testRoot 'install-recovery-junction-target'
    [System.IO.Directory]::CreateDirectory($installJunctionDataRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($installJunctionTarget) | Out-Null
    [System.IO.Directory]::CreateDirectory(
        (Split-Path -Parent $installJunctionShim)) | Out-Null
    '@echo off' | Set-Content -LiteralPath $installJunctionShim -Encoding ASCII
    '<configuration />' |
        Set-Content -LiteralPath $installJunctionConfig -Encoding UTF8
    $installJunctionRecoveryParent = Join-Path $installJunctionDataRoot 'release-recovery'
    $null = New-Item `
        -ItemType Junction `
        -Path $installJunctionRecoveryParent `
        -Target $installJunctionTarget
    $installJunctionTargetSignature = Get-TreeSignature -Root $installJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot $installJunctionInstallRoot `
                -DataRoot $installJunctionDataRoot `
                -InitialConfigSource $installJunctionConfig `
                -LauncherTargetPath $installJunctionLauncher `
                -PublicShimPath $installJunctionShim `
                -TestExternalStateRoot $installJunctionExternalRoot `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $installJunctionTarget) -ceq
                $installJunctionTargetSignature
        ) 'Installer wrote through a recovery-parent junction before rejecting it.'
    }
    finally {
        if ([System.IO.Directory]::Exists($installJunctionRecoveryParent)) {
            [System.IO.Directory]::Delete($installJunctionRecoveryParent, $false)
        }
    }
    Assert-True (
        -not (Test-Path `
            -LiteralPath (Join-Path $installJunctionInstallRoot $script:LhmExecutableName) `
            -PathType Leaf)
    ) 'Recovery-parent junction rejection left an installed executable.'
    Assert-NoTransactionDebris -InstallRoot $installJunctionInstallRoot

    $finalizerJunctionDataRoot = Join-Path $testRoot 'finalizer-junction-data'
    $finalizerJunctionTarget = Join-Path $testRoot 'finalizer-junction-target'
    [System.IO.Directory]::CreateDirectory($finalizerJunctionDataRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($finalizerJunctionTarget) | Out-Null
    $finalizerJunctionRecoveryParent =
        Join-Path $finalizerJunctionDataRoot 'release-recovery'
    $null = New-Item `
        -ItemType Junction `
        -Path $finalizerJunctionRecoveryParent `
        -Target $finalizerJunctionTarget
    $finalizerJunctionTargetSignature =
        Get-TreeSignature -Root $finalizerJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            $null = New-LhmSafeRecoveryPreparationDirectory `
                -DataRoot $finalizerJunctionDataRoot `
                -RecoveryParent $finalizerJunctionRecoveryParent `
                -PreparationPath (Join-Path `
                    $finalizerJunctionRecoveryParent `
                    ".legacy-root-task-cutover-$([guid]::NewGuid().ToString('N'))") `
                -RequiredLeafPrefix '.legacy-root-task-cutover-'
        }
        Assert-True (
            (Get-TreeSignature -Root $finalizerJunctionTarget) -ceq
                $finalizerJunctionTargetSignature
        ) 'Finalizer preparation wrote through a recovery-parent junction before rejecting it.'
    }
    finally {
        if ([System.IO.Directory]::Exists($finalizerJunctionRecoveryParent)) {
            [System.IO.Directory]::Delete($finalizerJunctionRecoveryParent, $false)
        }
    }

    $launcherJunctionCase = Join-Path $testRoot 'launcher-junction-case'
    $launcherJunctionInstallRoot = Join-Path $launcherJunctionCase 'install'
    $launcherJunctionDataRoot = Join-Path $launcherJunctionCase 'data'
    $launcherJunctionParent = Join-Path $launcherJunctionCase 'script-data'
    $launcherJunctionTarget = Join-Path $testRoot 'launcher-junction-target'
    [System.IO.Directory]::CreateDirectory($launcherJunctionCase) | Out-Null
    [System.IO.Directory]::CreateDirectory($launcherJunctionTarget) | Out-Null
    'launcher junction sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $launcherJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $null = New-Item `
        -ItemType Junction `
        -Path $launcherJunctionParent `
        -Target $launcherJunctionTarget
    $launcherJunctionTargetSignature = Get-TreeSignature -Root $launcherJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot $launcherJunctionInstallRoot `
                -DataRoot $launcherJunctionDataRoot `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $launcherJunctionParent `
                    'Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $launcherJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $launcherJunctionTarget) -ceq
                $launcherJunctionTargetSignature
        ) 'Installer wrote through a launcher-parent junction.'
        Assert-True (
            -not (Test-Path `
                -LiteralPath (Join-Path `
                    $launcherJunctionInstallRoot `
                    $script:LhmExecutableName) `
                -PathType Leaf)
        ) 'Launcher-parent junction rejection installed an executable.'
    }
    finally {
        if ([System.IO.Directory]::Exists($launcherJunctionParent)) {
            [System.IO.Directory]::Delete($launcherJunctionParent, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot $launcherJunctionInstallRoot

    $logJunctionCase = Join-Path $testRoot 'log-junction-case'
    $logJunctionDataRoot = Join-Path $logJunctionCase 'data'
    $logJunctionTarget = Join-Path $testRoot 'log-junction-target'
    $logJunctionPath = Join-Path $logJunctionDataRoot 'logs'
    [System.IO.Directory]::CreateDirectory($logJunctionDataRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($logJunctionTarget) | Out-Null
    'log junction sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $logJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $null = New-Item `
        -ItemType Junction `
        -Path $logJunctionPath `
        -Target $logJunctionTarget
    $logJunctionTargetSignature = Get-TreeSignature -Root $logJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot (Join-Path $logJunctionCase 'install') `
                -DataRoot $logJunctionDataRoot `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $logJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $logJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $logJunctionTarget) -ceq
                $logJunctionTargetSignature
        ) 'Installer wrote through a log-directory junction.'
    }
    finally {
        if ([System.IO.Directory]::Exists($logJunctionPath)) {
            [System.IO.Directory]::Delete($logJunctionPath, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot (Join-Path $logJunctionCase 'install')

    $settingsReparseCase = Join-Path $testRoot 'settings-reparse-case'
    $settingsReparseDataRoot = Join-Path $settingsReparseCase 'data'
    $settingsReparseTargetRoot = Join-Path $testRoot 'settings-reparse-target'
    [System.IO.Directory]::CreateDirectory($settingsReparseDataRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($settingsReparseTargetRoot) | Out-Null
    $settingsReparseTarget = Join-Path $settingsReparseTargetRoot 'outside.config'
    [System.IO.Directory]::CreateDirectory($settingsReparseTarget) | Out-Null
    'settings reparse sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $settingsReparseTarget 'sentinel.txt') `
            -Encoding UTF8
    $settingsReparseLink =
        Join-Path $settingsReparseDataRoot $script:LhmSettingsFileName
    $null = New-Item `
        -ItemType Junction `
        -Path $settingsReparseLink `
        -Target $settingsReparseTarget
    $settingsReparseTargetSignature =
        Get-TreeSignature -Root $settingsReparseTargetRoot
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot (Join-Path $settingsReparseCase 'install') `
                -DataRoot $settingsReparseDataRoot `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $settingsReparseCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $settingsReparseCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $settingsReparseTargetRoot) -ceq
                $settingsReparseTargetSignature
        ) 'Installer wrote through a settings-file reparse point.'
    }
    finally {
        if ([System.IO.Directory]::Exists($settingsReparseLink)) {
            [System.IO.Directory]::Delete($settingsReparseLink, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot (Join-Path $settingsReparseCase 'install')

    $candidateJunctionCase = Join-Path $testRoot 'candidate-junction-case'
    $candidateJunctionTarget = Join-Path $testRoot 'candidate-junction-target'
    Copy-Item -LiteralPath $candidate1 -Destination $candidateJunctionTarget -Recurse
    'candidate junction sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $candidateJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $candidateJunctionPath = Join-Path $testRoot 'candidate-junction-link'
    $null = New-Item `
        -ItemType Junction `
        -Path $candidateJunctionPath `
        -Target $candidateJunctionTarget
    $candidateJunctionTargetSignature =
        Get-TreeSignature -Root $candidateJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidateJunctionPath `
                -InstallRoot (Join-Path $candidateJunctionCase 'install') `
                -DataRoot (Join-Path $candidateJunctionCase 'data') `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $candidateJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $candidateJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $candidateJunctionTarget) -ceq
                $candidateJunctionTargetSignature
        ) 'Candidate-directory junction validation changed its external target.'
        Assert-True (
            -not (Test-Path `
                -LiteralPath (Join-Path `
                    $candidateJunctionCase `
                    "install\$($script:LhmExecutableName)") `
                -PathType Leaf)
        ) 'Candidate-directory junction validation installed an executable.'
    }
    finally {
        if ([System.IO.Directory]::Exists($candidateJunctionPath)) {
            [System.IO.Directory]::Delete($candidateJunctionPath, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot (Join-Path $candidateJunctionCase 'install')

    $candidateFileReparseCase = Join-Path $testRoot 'candidate-file-reparse-case'
    $candidateFileReparseRoot = Join-Path $testRoot 'candidate-file-reparse'
    $candidateFileReparseTarget = Join-Path $testRoot 'candidate-file-reparse-target'
    Copy-Item -LiteralPath $candidate1 -Destination $candidateFileReparseRoot -Recurse
    [System.IO.Directory]::CreateDirectory($candidateFileReparseTarget) | Out-Null
    $candidateFileLink =
        Join-Path $candidateFileReparseRoot $script:LhmExecutableName
    $candidateFileTarget =
        Join-Path $candidateFileReparseTarget $script:LhmExecutableName
    [System.IO.Directory]::CreateDirectory($candidateFileTarget) | Out-Null
    Move-Item `
        -LiteralPath $candidateFileLink `
        -Destination (Join-Path $candidateFileTarget 'original.exe')
    'candidate file sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $candidateFileReparseTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $null = New-Item `
        -ItemType Junction `
        -Path $candidateFileLink `
        -Target $candidateFileTarget
    $candidateFileReparseTargetSignature =
        Get-TreeSignature -Root $candidateFileReparseTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidateFileReparseRoot `
                -InstallRoot (Join-Path $candidateFileReparseCase 'install') `
                -DataRoot (Join-Path $candidateFileReparseCase 'data') `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $candidateFileReparseCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $candidateFileReparseCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $candidateFileReparseTarget) -ceq
                $candidateFileReparseTargetSignature
        ) 'Candidate-file reparse validation changed its external target.'
        Assert-True (
            -not (Test-Path `
                -LiteralPath (Join-Path `
                    $candidateFileReparseCase `
                    "install\$($script:LhmExecutableName)") `
                -PathType Leaf)
        ) 'Candidate-file reparse validation installed an executable.'
    }
    finally {
        if ([System.IO.Directory]::Exists($candidateFileLink)) {
            [System.IO.Directory]::Delete($candidateFileLink, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot (Join-Path $candidateFileReparseCase 'install')

    $installRollbackJunctionCase = Join-Path $testRoot 'install-rollback-junction-case'
    $installRollbackJunctionRoot = Join-Path $installRollbackJunctionCase 'install'
    $installRollbackJunctionTarget =
        Join-Path $testRoot 'install-rollback-junction-target'
    [System.IO.Directory]::CreateDirectory($installRollbackJunctionRoot) | Out-Null
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $candidate1 `
        -DestinationDirectory $installRollbackJunctionRoot
    $installRollbackCurrentHash =
        Get-LhmFileSha256 -Path (
            Join-Path $installRollbackJunctionRoot $script:LhmExecutableName)
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $candidate2 `
        -DestinationDirectory $installRollbackJunctionTarget
    'install rollback sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $installRollbackJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $installRollbackJunction =
        Join-Path $installRollbackJunctionRoot 'rollback'
    $null = New-Item `
        -ItemType Junction `
        -Path $installRollbackJunction `
        -Target $installRollbackJunctionTarget
    $installRollbackJunctionTargetSignature =
        Get-TreeSignature -Root $installRollbackJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate2 `
                -InstallRoot $installRollbackJunctionRoot `
                -DataRoot (Join-Path $installRollbackJunctionCase 'data') `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $installRollbackJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $installRollbackJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $installRollbackJunctionTarget) -ceq
                $installRollbackJunctionTargetSignature
        ) 'Installer rollback-junction validation changed its external target.'
        Assert-True (
            (Get-LhmFileSha256 -Path (
                Join-Path $installRollbackJunctionRoot $script:LhmExecutableName)) -ceq
                $installRollbackCurrentHash
        ) 'Installer rollback-junction validation changed the current payload.'
    }
    finally {
        if ([System.IO.Directory]::Exists($installRollbackJunction)) {
            [System.IO.Directory]::Delete($installRollbackJunction, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot $installRollbackJunctionRoot

    $transactionJunctionCase = Join-Path $testRoot 'transaction-junction-case'
    $transactionJunctionInstallRoot = Join-Path $transactionJunctionCase 'install'
    $transactionJunctionTarget = Join-Path $testRoot 'transaction-junction-target'
    [System.IO.Directory]::CreateDirectory($transactionJunctionInstallRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($transactionJunctionTarget) | Out-Null
    'transaction junction sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $transactionJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $transactionJunction =
        Join-Path $transactionJunctionInstallRoot '.release-transaction'
    $null = New-Item `
        -ItemType Junction `
        -Path $transactionJunction `
        -Target $transactionJunctionTarget
    $transactionJunctionTargetSignature =
        Get-TreeSignature -Root $transactionJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot $transactionJunctionInstallRoot `
                -DataRoot (Join-Path $transactionJunctionCase 'data') `
                -InitialConfigSource $initialConfig `
                -LauncherTargetPath (Join-Path `
                    $transactionJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $transactionJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $transactionJunctionTarget) -ceq
                $transactionJunctionTargetSignature
        ) 'Transaction-junction validation changed its external target.'
    }
    finally {
        if ([System.IO.Directory]::Exists($transactionJunction)) {
            [System.IO.Directory]::Delete($transactionJunction, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot $transactionJunctionInstallRoot

    $restoreInstallJunctionCase = Join-Path $testRoot 'restore-install-junction-case'
    $restoreInstallJunctionTarget =
        Join-Path $testRoot 'restore-install-junction-target'
    [System.IO.Directory]::CreateDirectory($restoreInstallJunctionCase) | Out-Null
    [System.IO.Directory]::CreateDirectory($restoreInstallJunctionTarget) | Out-Null
    'restore install sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $restoreInstallJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $restoreInstallJunction = Join-Path $restoreInstallJunctionCase 'install'
    $null = New-Item `
        -ItemType Junction `
        -Path $restoreInstallJunction `
        -Target $restoreInstallJunctionTarget
    $restoreInstallJunctionTargetSignature =
        Get-TreeSignature -Root $restoreInstallJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $rollbackScript `
                -InstallRoot $restoreInstallJunction `
                -DataRoot (Join-Path $restoreInstallJunctionCase 'data') `
                -LauncherTargetPath (Join-Path `
                    $restoreInstallJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $restoreInstallJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $restoreInstallJunctionTarget) -ceq
                $restoreInstallJunctionTargetSignature
        ) 'Restore install-root junction validation changed its external target.'
    }
    finally {
        if ([System.IO.Directory]::Exists($restoreInstallJunction)) {
            [System.IO.Directory]::Delete($restoreInstallJunction, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot $restoreInstallJunction

    $restoreRollbackJunctionCase =
        Join-Path $testRoot 'restore-rollback-junction-case'
    $restoreRollbackJunctionRoot =
        Join-Path $restoreRollbackJunctionCase 'install'
    $restoreRollbackJunctionTarget =
        Join-Path $testRoot 'restore-rollback-junction-target'
    [System.IO.Directory]::CreateDirectory($restoreRollbackJunctionRoot) | Out-Null
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $candidate1 `
        -DestinationDirectory $restoreRollbackJunctionRoot
    $restoreRollbackCurrentHash =
        Get-LhmFileSha256 -Path (
            Join-Path $restoreRollbackJunctionRoot $script:LhmExecutableName)
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $candidate2 `
        -DestinationDirectory $restoreRollbackJunctionTarget
    'restore rollback sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $restoreRollbackJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $restoreRollbackJunction =
        Join-Path $restoreRollbackJunctionRoot 'rollback'
    $null = New-Item `
        -ItemType Junction `
        -Path $restoreRollbackJunction `
        -Target $restoreRollbackJunctionTarget
    $restoreRollbackJunctionTargetSignature =
        Get-TreeSignature -Root $restoreRollbackJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            & $rollbackScript `
                -InstallRoot $restoreRollbackJunctionRoot `
                -DataRoot (Join-Path $restoreRollbackJunctionCase 'data') `
                -LauncherTargetPath (Join-Path `
                    $restoreRollbackJunctionCase `
                    'script-data\Start-LibreHardwareMonitor.ps1') `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot (Join-Path $restoreRollbackJunctionCase 'external') `
                -NonLiveTestMode `
                -Confirm:$false
        }
        Assert-True (
            (Get-TreeSignature -Root $restoreRollbackJunctionTarget) -ceq
                $restoreRollbackJunctionTargetSignature
        ) 'Restore rollback-junction validation changed its external target.'
        Assert-True (
            (Get-LhmFileSha256 -Path (
                Join-Path $restoreRollbackJunctionRoot $script:LhmExecutableName)) -ceq
                $restoreRollbackCurrentHash
        ) 'Restore rollback-junction validation changed the current payload.'
    }
    finally {
        if ([System.IO.Directory]::Exists($restoreRollbackJunction)) {
            [System.IO.Directory]::Delete($restoreRollbackJunction, $false)
        }
    }
    Assert-NoTransactionDebris -InstallRoot $restoreRollbackJunctionRoot

    $ownedDirectoryParent = Join-Path $testRoot 'owned-directory-guards'
    $ownedDirectoryTarget = Join-Path $testRoot 'owned-directory-target'
    [System.IO.Directory]::CreateDirectory($ownedDirectoryParent) | Out-Null
    [System.IO.Directory]::CreateDirectory($ownedDirectoryTarget) | Out-Null
    'owned target sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $ownedDirectoryTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $ownedDirectoryJunction =
        Join-Path `
            $ownedDirectoryParent `
            ".release-preparing-$([guid]::NewGuid().ToString('N'))"
    $null = New-Item `
        -ItemType Junction `
        -Path $ownedDirectoryJunction `
        -Target $ownedDirectoryTarget
    $ownedDirectoryTargetSignature = Get-TreeSignature -Root $ownedDirectoryTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            Remove-LhmOwnedDirectory `
                -Path $ownedDirectoryJunction `
                -ExpectedParent $ownedDirectoryParent `
                -RequiredLeafPrefix '.release-preparing-'
        }
        Assert-True (
            (Get-TreeSignature -Root $ownedDirectoryTarget) -ceq
                $ownedDirectoryTargetSignature
        ) 'Owned-directory target-junction validation changed its external target.'
    }
    finally {
        if ([System.IO.Directory]::Exists($ownedDirectoryJunction)) {
            [System.IO.Directory]::Delete($ownedDirectoryJunction, $false)
        }
    }

    $ownedDirectoryWithChild =
        Join-Path `
            $ownedDirectoryParent `
            ".release-preparing-$([guid]::NewGuid().ToString('N'))"
    $ownedDirectoryChildTarget = Join-Path $testRoot 'owned-directory-child-target'
    [System.IO.Directory]::CreateDirectory($ownedDirectoryWithChild) | Out-Null
    [System.IO.Directory]::CreateDirectory($ownedDirectoryChildTarget) | Out-Null
    'owned child sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $ownedDirectoryChildTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $ownedDirectoryChildJunction =
        Join-Path $ownedDirectoryWithChild 'redirected-child'
    $null = New-Item `
        -ItemType Junction `
        -Path $ownedDirectoryChildJunction `
        -Target $ownedDirectoryChildTarget
    $ownedDirectoryChildTargetSignature =
        Get-TreeSignature -Root $ownedDirectoryChildTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            Remove-LhmOwnedDirectory `
                -Path $ownedDirectoryWithChild `
                -ExpectedParent $ownedDirectoryParent `
                -RequiredLeafPrefix '.release-preparing-'
        }
        Assert-True (
            (Get-TreeSignature -Root $ownedDirectoryChildTarget) -ceq
                $ownedDirectoryChildTargetSignature
        ) 'Owned-directory child-junction validation changed its external target.'
        Assert-True (
            Test-Path -LiteralPath $ownedDirectoryWithChild -PathType Container
        ) 'Owned-directory child-junction validation removed its guarded root.'
    }
    finally {
        if ([System.IO.Directory]::Exists($ownedDirectoryChildJunction)) {
            [System.IO.Directory]::Delete($ownedDirectoryChildJunction, $false)
        }
        if ([System.IO.Directory]::Exists($ownedDirectoryWithChild)) {
            [System.IO.Directory]::Delete($ownedDirectoryWithChild, $true)
        }
    }
    Assert-NoTransactionDebris -InstallRoot $ownedDirectoryParent

    $ownerlessRoot = Join-Path $testRoot 'ownerless-first-install'
    $ownerlessInstallRoot = Join-Path $ownerlessRoot 'install'
    $ownerlessDataRoot = Join-Path $ownerlessRoot 'data'
    $ownerlessExternalRoot = Join-Path $ownerlessRoot 'external-state'
    $ownerlessLauncher = Join-Path $ownerlessRoot 'script-data\Start-LibreHardwareMonitor.ps1'
    $ownerlessShim = Join-Path $ownerlessRoot 'bin\librehw.cmd'
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $ownerlessShim)) | Out-Null
    '@echo off' | Set-Content -LiteralPath $ownerlessShim -Encoding ASCII
    $ownerlessShimHash = Get-LhmFileSha256 -Path $ownerlessShim

    $null = & $installScript `
        -CandidateDirectory $candidate1 `
        -InstallRoot $ownerlessInstallRoot `
        -DataRoot $ownerlessDataRoot `
        -InitialConfigSource $initialConfig `
        -LauncherTargetPath $ownerlessLauncher `
        -PublicShimPath $ownerlessShim `
        -TestExternalStateRoot $ownerlessExternalRoot `
        -NonLiveTestMode `
        -Confirm:$false
    $ownerlessRecoveryRoot =
        Join-Path $ownerlessDataRoot 'release-recovery\pre-stable-startup'
    $ownerlessRecovery = Read-LhmPreStableRecoveryPacket `
        -RecoveryRoot $ownerlessRecoveryRoot `
        -ExpectedLauncherTargetPath $ownerlessLauncher `
        -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
        -ExpectedPublicShimPath $ownerlessShim `
        -ExpectedPublicShimSha256 $ownerlessShimHash `
        -NonLiveTestMode `
        -TestExternalStateRoot $ownerlessExternalRoot
    Assert-True (
        -not [bool]$ownerlessRecovery.Manifest.launcherExisted -and
        -not [bool]$ownerlessRecovery.Manifest.managedTaskExisted
    ) 'Ownerless first install did not record both startup owners as absent.'
    Assert-True (
        @(Get-ChildItem -LiteralPath $ownerlessRecoveryRoot -Force).Count -eq 1
    ) 'Ownerless first install retained an unexpected recovery backup.'
    $asymmetricRecoveryRoot = Join-Path $ownerlessRoot 'asymmetric-recovery'
    Copy-Item `
        -LiteralPath $ownerlessRecoveryRoot `
        -Destination $asymmetricRecoveryRoot `
        -Recurse
    $asymmetricLauncherBackup = Join-Path $asymmetricRecoveryRoot 'launcher-backup.ps1'
    'legacy launcher' | Set-Content -LiteralPath $asymmetricLauncherBackup -Encoding UTF8
    $asymmetricLauncherHash = Get-LhmFileSha256 -Path $asymmetricLauncherBackup
    $asymmetricManifestPath = Join-Path $asymmetricRecoveryRoot 'recovery.json'
    $asymmetricManifest =
        Get-Content -LiteralPath $asymmetricManifestPath -Raw | ConvertFrom-Json
    $asymmetricManifest.launcherExisted = $true
    $asymmetricManifest.launcherBackup = 'launcher-backup.ps1'
    $asymmetricManifest.launcherSha256 = $asymmetricLauncherHash
    $asymmetricManifest | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath $asymmetricManifestPath -Encoding UTF8
    Assert-Throws -MessagePattern 'both discovered startup owners or record both as absent' -Action {
        $null = Read-LhmPreStableRecoveryPacket `
            -RecoveryRoot $asymmetricRecoveryRoot `
            -ExpectedLauncherTargetPath $ownerlessLauncher `
            -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedPublicShimPath $ownerlessShim `
            -ExpectedPublicShimSha256 $ownerlessShimHash `
            -ExpectedLauncherBackupSha256 $asymmetricLauncherHash `
            -NonLiveTestMode `
            -TestExternalStateRoot $ownerlessExternalRoot
    }
    Assert-True (
        Test-Path -LiteralPath (Join-Path $ownerlessInstallRoot $script:LhmExecutableName) -PathType Leaf
    ) 'Ownerless first install did not install the release executable.'
    Assert-NoTransactionDebris -InstallRoot $ownerlessInstallRoot

    $beforeWhatIf = Get-TreeSignature -Root $testRoot
    & $installScript `
        -CandidateDirectory $candidate1 `
        -InstallRoot $installRoot `
        -DataRoot $dataRoot `
        -InitialConfigSource $initialConfig `
        -LauncherTargetPath $launcherTarget `
        -PublicShimPath $shimPath `
        -TestExternalStateRoot $externalRoot `
        -NonLiveTestMode `
        -WhatIf
    Assert-True ((Get-TreeSignature -Root $testRoot) -ceq $beforeWhatIf) '-WhatIf changed temporary state.'

    Assert-Throws -MessagePattern 'AfterTaskStage' -Action {
        & $installScript `
            -CandidateDirectory $candidate1 `
            -InstallRoot $installRoot `
            -DataRoot $dataRoot `
            -InitialConfigSource $initialConfig `
            -LauncherTargetPath $launcherTarget `
            -PublicShimPath $shimPath `
            -TestExternalStateRoot $externalRoot `
            -NonLiveTestMode `
            -TestFailurePoint AfterTaskStage `
            -Confirm:$false
    }
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installRoot $script:LhmExecutableName))) 'Failed first install left an EXE.'
    Assert-True ((Get-LhmFileSha256 -Path $launcherTarget) -ceq $legacyLauncherHash) 'Failed first install did not restore launcher.'
    Assert-True ((Get-LhmFileSha256 -Path (Join-Path $externalRoot 'managed-task.json')) -ceq $legacyTaskHash) 'Failed first install did not restore task state.'
    Assert-NoTransactionDebris -InstallRoot $installRoot

    Assert-Throws -MessagePattern 'AfterHealth' -Action {
        & $installScript `
            -CandidateDirectory $candidate1 `
            -InstallRoot $installRoot `
            -DataRoot $dataRoot `
            -InitialConfigSource $initialConfig `
            -LauncherTargetPath $launcherTarget `
            -PublicShimPath $shimPath `
            -TestExternalStateRoot $externalRoot `
            -NonLiveTestMode `
            -TestFailurePoint AfterHealth `
            -Confirm:$false
    }
    $preStableRecoveryRoot = Join-Path $dataRoot 'release-recovery\pre-stable-startup'
    Assert-True (Test-Path -LiteralPath $preStableRecoveryRoot -PathType Container) 'AfterHealth failure did not retain bounded pre-stable recovery.'
    $preStableRecoverySignature = Get-TreeSignature -Root $preStableRecoveryRoot
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installRoot $script:LhmExecutableName))) 'AfterHealth first-install recovery left an EXE.'
    Assert-True ((Get-LhmFileSha256 -Path $launcherTarget) -ceq $legacyLauncherHash) 'AfterHealth first-install recovery did not restore launcher.'
    Assert-True ((Get-LhmFileSha256 -Path (Join-Path $externalRoot 'managed-task.json')) -ceq $legacyTaskHash) 'AfterHealth first-install recovery did not restore task state.'
    Assert-NoTransactionDebris -InstallRoot $installRoot

    $null = Read-LhmPreStableRecoveryPacket `
        -RecoveryRoot $preStableRecoveryRoot `
        -ExpectedLauncherTargetPath $launcherTarget `
        -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
        -ExpectedPublicShimPath $shimPath `
        -ExpectedPublicShimSha256 $shimHash `
        -ExpectedLauncherBackupSha256 $legacyLauncherHash `
        -ExpectedManagedTaskBackupSha256 $legacyTaskHash `
        -NonLiveTestMode `
        -TestExternalStateRoot $externalRoot `
        -RequireExternalStateMatch

    $tamperedPreStableLauncherRoot =
        Join-Path $testRoot 'pre-stable-recovery-tampered-launcher'
    Copy-Item `
        -LiteralPath $preStableRecoveryRoot `
        -Destination $tamperedPreStableLauncherRoot `
        -Recurse
    $tamperedPreStableLauncherPath =
        Join-Path $tamperedPreStableLauncherRoot 'launcher-backup.ps1'
    'Start-Process C:\Windows\System32\cmd.exe' |
        Set-Content -LiteralPath $tamperedPreStableLauncherPath -Encoding UTF8
    $tamperedPreStableLauncherManifest =
        Get-Content `
            -LiteralPath (Join-Path $tamperedPreStableLauncherRoot 'recovery.json') `
            -Raw |
        ConvertFrom-Json
    $tamperedPreStableLauncherManifest.launcherSha256 =
        Get-LhmFileSha256 -Path $tamperedPreStableLauncherPath
    $tamperedPreStableLauncherManifest | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath (Join-Path $tamperedPreStableLauncherRoot 'recovery.json')
    Assert-Throws -MessagePattern 'trusted content' -Action {
        $null = Read-LhmPreStableRecoveryPacket `
            -RecoveryRoot $tamperedPreStableLauncherRoot `
            -ExpectedLauncherTargetPath $launcherTarget `
            -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -ExpectedLauncherBackupSha256 $legacyLauncherHash `
            -ExpectedManagedTaskBackupSha256 $legacyTaskHash `
            -NonLiveTestMode `
            -TestExternalStateRoot $externalRoot
    }

    $tamperedPreStableTaskRoot =
        Join-Path $testRoot 'pre-stable-recovery-tampered-task'
    Copy-Item `
        -LiteralPath $preStableRecoveryRoot `
        -Destination $tamperedPreStableTaskRoot `
        -Recurse
    $tamperedPreStableTaskPath =
        Join-Path $tamperedPreStableTaskRoot 'managed-task.test.json'
    '{"taskPath":"\\SevGrp\\AdminTask\\LibreHW-No-UAC","execute":"C:\\Windows\\System32\\cmd.exe"}' |
        Set-Content -LiteralPath $tamperedPreStableTaskPath -Encoding UTF8
    $tamperedPreStableTaskManifest =
        Get-Content `
            -LiteralPath (Join-Path $tamperedPreStableTaskRoot 'recovery.json') `
            -Raw |
        ConvertFrom-Json
    $tamperedPreStableTaskManifest.managedTaskSha256 =
        Get-LhmFileSha256 -Path $tamperedPreStableTaskPath
    $tamperedPreStableTaskManifest | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath (Join-Path $tamperedPreStableTaskRoot 'recovery.json')
    Assert-Throws -MessagePattern 'trusted content' -Action {
        $null = Read-LhmPreStableRecoveryPacket `
            -RecoveryRoot $tamperedPreStableTaskRoot `
            -ExpectedLauncherTargetPath $launcherTarget `
            -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -ExpectedLauncherBackupSha256 $legacyLauncherHash `
            -ExpectedManagedTaskBackupSha256 $legacyTaskHash `
            -NonLiveTestMode `
            -TestExternalStateRoot $externalRoot
    }

    $tamperedPreStablePathRoot =
        Join-Path $testRoot 'pre-stable-recovery-tampered-path'
    Copy-Item `
        -LiteralPath $preStableRecoveryRoot `
        -Destination $tamperedPreStablePathRoot `
        -Recurse
    $tamperedPreStablePathManifest =
        Get-Content `
            -LiteralPath (Join-Path $tamperedPreStablePathRoot 'recovery.json') `
            -Raw |
        ConvertFrom-Json
    $tamperedPreStablePathManifest.launcherBackup = '..\outside.ps1'
    $tamperedPreStablePathManifest | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath (Join-Path $tamperedPreStablePathRoot 'recovery.json')
    Assert-Throws -MessagePattern 'launcher backup' -Action {
        $null = Read-LhmPreStableRecoveryPacket `
            -RecoveryRoot $tamperedPreStablePathRoot `
            -ExpectedLauncherTargetPath $launcherTarget `
            -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -ExpectedLauncherBackupSha256 $legacyLauncherHash `
            -ExpectedManagedTaskBackupSha256 $legacyTaskHash `
            -NonLiveTestMode `
            -TestExternalStateRoot $externalRoot
    }

    $tamperedPreStableTypeRoot =
        Join-Path $testRoot 'pre-stable-recovery-tampered-type'
    Copy-Item `
        -LiteralPath $preStableRecoveryRoot `
        -Destination $tamperedPreStableTypeRoot `
        -Recurse
    $tamperedPreStableTypeManifest =
        Get-Content `
            -LiteralPath (Join-Path $tamperedPreStableTypeRoot 'recovery.json') `
            -Raw |
        ConvertFrom-Json
    $tamperedPreStableTypeManifest.managedTaskExisted = 'true'
    $tamperedPreStableTypeManifest | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath (Join-Path $tamperedPreStableTypeRoot 'recovery.json')
    Assert-Throws -MessagePattern 'JSON Boolean' -Action {
        $null = Read-LhmPreStableRecoveryPacket `
            -RecoveryRoot $tamperedPreStableTypeRoot `
            -ExpectedLauncherTargetPath $launcherTarget `
            -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -ExpectedLauncherBackupSha256 $legacyLauncherHash `
            -ExpectedManagedTaskBackupSha256 $legacyTaskHash `
            -NonLiveTestMode `
            -TestExternalStateRoot $externalRoot
    }

    $unexpectedPreStableEntryRoot =
        Join-Path $testRoot 'pre-stable-recovery-unexpected-entry'
    Copy-Item `
        -LiteralPath $preStableRecoveryRoot `
        -Destination $unexpectedPreStableEntryRoot `
        -Recurse
    'unexpected' | Set-Content `
        -LiteralPath (Join-Path $unexpectedPreStableEntryRoot 'extra.bin')
    Assert-Throws -MessagePattern 'unexpected or unsafe entries' -Action {
        $null = Read-LhmPreStableRecoveryPacket `
            -RecoveryRoot $unexpectedPreStableEntryRoot `
            -ExpectedLauncherTargetPath $launcherTarget `
            -ExpectedManagedTaskPath $script:LhmManagedTaskPath `
            -ExpectedPublicShimPath $shimPath `
            -ExpectedPublicShimSha256 $shimHash `
            -ExpectedLauncherBackupSha256 $legacyLauncherHash `
            -ExpectedManagedTaskBackupSha256 $legacyTaskHash `
            -NonLiveTestMode `
            -TestExternalStateRoot $externalRoot
    }

    $null = & $installScript `
        -CandidateDirectory $candidate1 `
        -InstallRoot $installRoot `
        -DataRoot $dataRoot `
        -InitialConfigSource $initialConfig `
        -LauncherTargetPath $launcherTarget `
        -PublicShimPath $shimPath `
        -TestExternalStateRoot $externalRoot `
        -NonLiveTestMode `
        -Confirm:$false
    Assert-True (
        (Get-TreeSignature -Root $preStableRecoveryRoot) -ceq $preStableRecoverySignature
    ) 'First-install retry replaced or changed the reusable pre-stable recovery packet.'
    $release1 = Read-LhmReleasePayload -Directory $installRoot
    Assert-True ($release1.Manifest.releaseId -ceq 'one-abcde01') 'First release was not installed.'
    Assert-True ((Get-LhmFileSha256 -Path $launcherTarget) -ceq (Get-LhmFileSha256 -Path $canonicalLauncher)) 'Canonical launcher was not installed.'
    Assert-True ((Get-LhmFileSha256 -Path $shimPath) -ceq $shimHash) 'Public shim changed.'
    $runtimePath = Join-Path $installRoot $script:LhmRuntimeConfigName
    $runtimeHash = Get-LhmFileSha256 -Path $runtimePath
    $configHash = Get-LhmFileSha256 -Path (Join-Path $dataRoot $script:LhmSettingsFileName)
    'do-not-delete' | Set-Content -LiteralPath (Join-Path $dataRoot 'logs\sentinel.csv')

    $crashInstallRoot = Join-Path $testRoot 'crash-install'
    $crashDataRoot = Join-Path $testRoot 'crash-data'
    $crashExternalRoot = Join-Path $testRoot 'crash-external-state'
    $crashLauncherTarget = Join-Path $testRoot 'crash-script-data\Start-LibreHardwareMonitor.ps1'
    $crashShimPath = Join-Path $testRoot 'crash-bin\librehw.cmd'
    $crashConfig = Join-Path $testRoot 'crash-initial.config'
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $crashLauncherTarget)) | Out-Null
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $crashShimPath)) | Out-Null
    [System.IO.Directory]::CreateDirectory($crashExternalRoot) | Out-Null
    'crash legacy launcher' | Set-Content -LiteralPath $crashLauncherTarget
    '@echo off' | Set-Content -LiteralPath $crashShimPath -Encoding ASCII
    '{"crashLegacy":true}' | Set-Content `
        -LiteralPath (Join-Path $crashExternalRoot 'managed-task.json')
    '<configuration />' | Set-Content -LiteralPath $crashConfig

    Assert-Throws -MessagePattern 'AfterHealth' -Action {
        & $installScript `
            -CandidateDirectory $candidate1 `
            -InstallRoot $crashInstallRoot `
            -DataRoot $crashDataRoot `
            -InitialConfigSource $crashConfig `
            -LauncherTargetPath $crashLauncherTarget `
            -PublicShimPath $crashShimPath `
            -TestExternalStateRoot $crashExternalRoot `
            -NonLiveTestMode `
            -TestFailurePoint AfterHealth `
            -TestSimulateCrash `
            -Confirm:$false
    }
    Assert-True (
        Test-Path -LiteralPath (Join-Path $crashInstallRoot '.release-transaction') -PathType Container
    ) 'Simulated crash did not leave the transaction journal.'
    $crashRecoveryRoot = Join-Path $crashDataRoot 'release-recovery\pre-stable-startup'
    Assert-True (
        Test-Path -LiteralPath $crashRecoveryRoot -PathType Container
    ) 'Simulated crash did not retain pre-stable recovery.'
    $crashRecoverySignature = Get-TreeSignature -Root $crashRecoveryRoot

    $null = & $installScript `
        -CandidateDirectory $candidate1 `
        -InstallRoot $crashInstallRoot `
        -DataRoot $crashDataRoot `
        -InitialConfigSource $crashConfig `
        -LauncherTargetPath $crashLauncherTarget `
        -PublicShimPath $crashShimPath `
        -TestExternalStateRoot $crashExternalRoot `
        -NonLiveTestMode `
        -Confirm:$false
    $crashInstalled = Read-LhmReleasePayload -Directory $crashInstallRoot
    Assert-True ($crashInstalled.Manifest.releaseId -ceq 'one-abcde01') 'Crash retry did not complete the first installation.'
    Assert-True (
        (Get-TreeSignature -Root $crashRecoveryRoot) -ceq $crashRecoverySignature
    ) 'Crash retry replaced or changed the reusable recovery packet.'
    Assert-NoTransactionDebris -InstallRoot $crashInstallRoot

    $null = & $installScript `
        -CandidateDirectory $candidate2 `
        -InstallRoot $installRoot `
        -DataRoot $dataRoot `
        -LauncherTargetPath $launcherTarget `
        -PublicShimPath $shimPath `
        -TestExternalStateRoot $externalRoot `
        -NonLiveTestMode `
        -Confirm:$false
    $release2 = Read-LhmReleasePayload -Directory $installRoot
    $rollback1 = Read-LhmReleasePayload -Directory (Join-Path $installRoot 'rollback') -RequireCandidateShape
    Assert-True ($release2.Manifest.releaseId -ceq 'two-abcde02') 'Second release was not installed.'
    Assert-True ($rollback1.Manifest.releaseId -ceq 'one-abcde01') 'Exactly one prior release was not retained.'
    Assert-True ((Get-LhmFileSha256 -Path $runtimePath) -ceq $runtimeHash) 'Runtime descriptor changed during promotion.'
    Assert-True ((Get-LhmFileSha256 -Path (Join-Path $dataRoot $script:LhmSettingsFileName)) -ceq $configHash) 'Config changed during promotion.'
    Assert-True (Test-Path -LiteralPath (Join-Path $dataRoot 'logs\sentinel.csv')) 'Log sentinel was deleted.'

    $stableBaseline = Get-TreeSignature -Root $installRoot
    $externalBaseline = Get-TreeSignature -Root $externalRoot
    $launcherBaseline = Get-LhmFileSha256 -Path $launcherTarget

    $recoveryPreparation =
        Join-Path $installRoot ".release-preparing-$([guid]::NewGuid().ToString('N'))"
    [System.IO.Directory]::CreateDirectory($recoveryPreparation) | Out-Null
    $null = Copy-LhmPayloadPair `
        -SourceDirectory $installRoot `
        -DestinationDirectory (Join-Path $recoveryPreparation 'previous-current')
    $null = Copy-LhmPayloadPair `
        -SourceDirectory (Join-Path $installRoot 'rollback') `
        -DestinationDirectory (Join-Path $recoveryPreparation 'previous-rollback')
    Copy-Item `
        -LiteralPath $launcherTarget `
        -Destination (Join-Path $recoveryPreparation 'launcher-backup.ps1')
    Copy-Item `
        -LiteralPath (Join-Path $externalRoot 'managed-task.json') `
        -Destination (Join-Path $recoveryPreparation 'managed-task.test.json')
    $interruptedState = @{
        operation = 'promote'
        phase = 'candidate-installed'
        installRoot = $installRoot
        launcherTargetPath = $launcherTarget
        publicShimPath = $shimPath
        publicShimSha256 = $shimHash
        hadCurrent = $true
        hadRollback = $true
        hadLauncher = $true
        managedTaskExisted = $true
        createdAt = [DateTimeOffset]::UtcNow.ToString('o')
    }
    Write-LhmTransactionState `
        -TransactionRoot $recoveryPreparation `
        -State $interruptedState
    Move-Item `
        -LiteralPath $recoveryPreparation `
        -Destination (Join-Path $installRoot '.release-transaction')

    Clear-LhmPayloadPair -Directory $installRoot
    Clear-LhmPayloadPair -Directory (Join-Path $installRoot 'rollback')
    $null = Copy-LhmPayloadPair `
        -SourceDirectory (Join-Path $installRoot '.release-transaction\previous-rollback') `
        -DestinationDirectory $installRoot `
        -DestinationMayContainOtherEntries
    $null = Copy-LhmPayloadPair `
        -SourceDirectory (Join-Path $installRoot '.release-transaction\previous-current') `
        -DestinationDirectory (Join-Path $installRoot 'rollback')
    'interrupted launcher' | Set-Content -LiteralPath $launcherTarget
    '{"interrupted":true}' | Set-Content `
        -LiteralPath (Join-Path $externalRoot 'managed-task.json')

    $interruptedWhatIfBaseline = Get-TreeSignature -Root $testRoot
    & $installScript `
        -CandidateDirectory $candidate1 `
        -InstallRoot $installRoot `
        -DataRoot $dataRoot `
        -LauncherTargetPath $launcherTarget `
        -PublicShimPath $shimPath `
        -TestExternalStateRoot $externalRoot `
        -NonLiveTestMode `
        -WhatIf
    Assert-True (
        (Get-TreeSignature -Root $testRoot) -ceq $interruptedWhatIfBaseline
    ) '-WhatIf changed an interrupted transaction or its external state.'

    $repairLauncherParent = Split-Path -Parent $launcherTarget
    $repairLauncherParentBackup = "$repairLauncherParent.normal"
    $repairLauncherJunctionTarget = Join-Path $testRoot 'repair-launcher-junction-target'
    Move-Item -LiteralPath $repairLauncherParent -Destination $repairLauncherParentBackup
    [System.IO.Directory]::CreateDirectory($repairLauncherJunctionTarget) | Out-Null
    'repair launcher junction sentinel' |
        Set-Content `
            -LiteralPath (Join-Path $repairLauncherJunctionTarget 'outside-sentinel.txt') `
            -Encoding UTF8
    $null = New-Item `
        -ItemType Junction `
        -Path $repairLauncherParent `
        -Target $repairLauncherJunctionTarget
    $repairLauncherJunctionTargetSignature =
        Get-TreeSignature -Root $repairLauncherJunctionTarget
    try {
        Assert-Throws -MessagePattern 'reparse point' -Action {
            $null = Repair-LhmInterruptedTransaction `
                -InstallRoot $installRoot `
                -LauncherTargetPath $launcherTarget `
                -PublicShimPath $shimPath `
                -NonLiveTestMode `
                -TestExternalStateRoot $externalRoot
        }
        Assert-True (
            (Get-TreeSignature -Root $repairLauncherJunctionTarget) -ceq
                $repairLauncherJunctionTargetSignature
        ) 'Interrupted repair wrote through a launcher-parent junction.'
        Assert-True (
            Test-Path `
                -LiteralPath (Join-Path $installRoot '.release-transaction') `
                -PathType Container
        ) 'Rejected launcher-junction repair removed its transaction journal.'
    }
    finally {
        if ([System.IO.Directory]::Exists($repairLauncherParent)) {
            [System.IO.Directory]::Delete($repairLauncherParent, $false)
        }
        Move-Item `
            -LiteralPath $repairLauncherParentBackup `
            -Destination $repairLauncherParent
    }

    $null = Repair-LhmInterruptedTransaction `
        -InstallRoot $installRoot `
        -LauncherTargetPath $launcherTarget `
        -PublicShimPath $shimPath `
        -NonLiveTestMode `
        -TestExternalStateRoot $externalRoot
    Assert-True ((Get-TreeSignature -Root $installRoot) -ceq $stableBaseline) 'Interrupted transaction recovery did not restore payload state.'
    Assert-True ((Get-TreeSignature -Root $externalRoot) -ceq $externalBaseline) 'Interrupted transaction recovery did not restore task state.'
    Assert-True ((Get-LhmFileSha256 -Path $launcherTarget) -ceq $launcherBaseline) 'Interrupted transaction recovery did not restore launcher state.'
    Assert-NoTransactionDebris -InstallRoot $installRoot

    foreach ($failurePoint in @('AfterPreviousRemoval', 'AfterCandidateInstall', 'AfterTaskStage', 'AfterHealth')) {
        Assert-Throws -MessagePattern $failurePoint -Action {
            & $installScript `
                -CandidateDirectory $candidate1 `
                -InstallRoot $installRoot `
                -DataRoot $dataRoot `
                -LauncherTargetPath $launcherTarget `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot $externalRoot `
                -NonLiveTestMode `
                -TestFailurePoint $failurePoint `
                -Confirm:$false
        }
        Assert-True ((Get-TreeSignature -Root $installRoot) -ceq $stableBaseline) "Promotion recovery failed at $failurePoint."
        Assert-True ((Get-TreeSignature -Root $externalRoot) -ceq $externalBaseline) "Task recovery failed at $failurePoint."
        Assert-NoTransactionDebris -InstallRoot $installRoot
    }

    $invalidCandidate = Join-Path $testRoot 'candidate-invalid'
    Copy-Item -LiteralPath $candidate1 -Destination $invalidCandidate -Recurse
    Add-Content `
        -LiteralPath (Join-Path $invalidCandidate $script:LhmExecutableName) `
        -Value 'tamper'
    Assert-Throws -MessagePattern 'hash mismatch' -Action {
        & $installScript `
            -CandidateDirectory $invalidCandidate `
            -InstallRoot $installRoot `
            -DataRoot $dataRoot `
            -LauncherTargetPath $launcherTarget `
            -PublicShimPath $shimPath `
            -TestExternalStateRoot $externalRoot `
            -NonLiveTestMode `
            -Confirm:$false
    }
    Assert-True ((Get-TreeSignature -Root $installRoot) -ceq $stableBaseline) 'Invalid candidate changed installed state.'

    'junk' | Set-Content -LiteralPath (Join-Path $installRoot 'unexpected.txt')
    Assert-Throws -MessagePattern 'unexpected entries' -Action {
        & $installScript `
            -CandidateDirectory $candidate1 `
            -InstallRoot $installRoot `
            -DataRoot $dataRoot `
            -LauncherTargetPath $launcherTarget `
            -PublicShimPath $shimPath `
            -TestExternalStateRoot $externalRoot `
            -NonLiveTestMode `
            -Confirm:$false
    }
    Remove-Item -LiteralPath (Join-Path $installRoot 'unexpected.txt')

    $null = & $rollbackScript `
        -InstallRoot $installRoot `
        -DataRoot $dataRoot `
        -LauncherTargetPath $launcherTarget `
        -PublicShimPath $shimPath `
        -TestExternalStateRoot $externalRoot `
        -NonLiveTestMode `
        -Confirm:$false
    $rolledBack = Read-LhmReleasePayload -Directory $installRoot
    $postRollbackSlot = Read-LhmReleasePayload `
        -Directory (Join-Path $installRoot 'rollback') `
        -RequireCandidateShape
    Assert-True ($rolledBack.Manifest.releaseId -ceq 'one-abcde01') 'Rollback did not restore release one.'
    Assert-True ($postRollbackSlot.Manifest.releaseId -ceq 'two-abcde02') 'Rollback did not retain release two as the one slot.'

    $rollbackBaseline = Get-TreeSignature -Root $installRoot
    foreach ($failurePoint in @('AfterPreviousRemoval', 'AfterCandidateInstall', 'AfterTaskStage', 'AfterHealth')) {
        Assert-Throws -MessagePattern $failurePoint -Action {
            & $rollbackScript `
                -InstallRoot $installRoot `
                -DataRoot $dataRoot `
                -LauncherTargetPath $launcherTarget `
                -PublicShimPath $shimPath `
                -TestExternalStateRoot $externalRoot `
                -NonLiveTestMode `
                -TestFailurePoint $failurePoint `
                -Confirm:$false
        }
        Assert-True ((Get-TreeSignature -Root $installRoot) -ceq $rollbackBaseline) "Rollback recovery failed at $failurePoint."
        Assert-NoTransactionDebris -InstallRoot $installRoot
    }

    Assert-True ((Get-LhmFileSha256 -Path $runtimePath) -ceq $runtimeHash) 'Runtime descriptor changed during rollback tests.'
    Assert-True ((Get-LhmFileSha256 -Path $shimPath) -ceq $shimHash) 'Public shim changed during tests.'
    Assert-True (Test-Path -LiteralPath (Join-Path $dataRoot 'logs\sentinel.csv')) 'Log sentinel was deleted during tests.'
    Assert-LhmInstalledRootInventory -InstallRoot $installRoot

    [pscustomobject]@{
        Result = 'PASS'
        TestRoot = $testRoot
        CurrentRelease = $rolledBack.Manifest.releaseId
        RollbackRelease = $postRollbackSlot.Manifest.releaseId
        FailureInjectionCases = 12
        HostileRecoveryManifestCases = 16
        HostileReparseCases = 12
        DataRootRelocationCases = 6
        RuntimeRootMigrationCases = 4
        LauncherConvergenceCases = 25
        ProductionTaskContractNegativeCases = 3
        ProductionHealthUriNegativeCases = 1
        InstallationIdentityNegativeCases = 2
        IdentityGateContractsVerified = $identityGateContractsVerified
        WindowsPowerShellLauncherCompatibility = $true
        WindowsPowerShellDataRootRelocationCompatibility = $true
        WindowsPowerShellCleanupCompatibility = $true
        JunctionParentCleanupGuard = $true
        NestedJunctionCleanupGuard = $true
        FilesystemRootCleanupGuard = $true
        RuntimeDescriptorPreserved = $true
        PublicShimPreserved = $true
        LogsPreserved = $true
    }
}
finally {
    $env:NUGET_PACKAGES = $oldNugetPackages
    $env:DOTNET_NOLOGO = $oldDotnetNoLogo
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $oldDotnetTelemetry
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = $oldDotnetCertificate
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTestRoot = Resolve-LhmFullPath -Path $testRoot
        $resolvedTempRoot = Resolve-LhmFullPath -Path ([System.IO.Path]::GetTempPath())
        if (-not (Test-LhmPathWithin -Path $resolvedTestRoot -Root $resolvedTempRoot) -or
            -not (Split-Path -Leaf $resolvedTestRoot).StartsWith('sq-librehw-release-test-', [System.StringComparison]::Ordinal)) {
            throw "Refusing to clean unexpected test root '$resolvedTestRoot'."
        }
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
