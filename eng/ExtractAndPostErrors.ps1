[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$BuildId = $env:BUILD_BUILDID,

    [Parameter(Mandatory = $false)]
    [string]$JobName = $env:AGENT_JOBNAME,

    [Parameter(Mandatory = $false)]
    [string]$BuildSourcesDirectory = $env:BUILD_SOURCESDIRECTORY,

    [Parameter(Mandatory = $false)]
    [string]$GitHubToken = $env:GITHUB_PAT,

    [Parameter(Mandatory = $false)]
    [string]$GitHubRepoOwner = $env:BUILD_REPOSITORY_NAME.Split('/')[0],

    [Parameter(Mandatory = $false)]
    [string]$GitHubRepoName = $env:BUILD_REPOSITORY_NAME.Split('/')[1],

    [Parameter(Mandatory = $false)]
    [string]$PullRequestId = $env:SYSTEM_PULLREQUEST_PULLREQUESTNUMBER,

    [Parameter(Mandatory = $false)]
    [string]$BuildConfiguration = $env:_BUILDCONFIG,

    [Parameter(Mandatory = $false)]
    [string]$BuildDefinitionName = $env:BUILD_DEFINITIONNAME
)

Write-Host "Starting error extraction for job: $JobName"

# Make sure this is a PR build
if (-not $PullRequestId) {
    Write-Host "Not a PR build, skipping error reporting."
    exit 0
}

# Make sure we have a GitHub token
if (-not $GitHubToken) {
    Write-Host "GitHub token not provided, skipping error reporting."
    exit 1
}

# Validate essential parameters
if (-not $BuildId) {
    Write-Host "Build ID not provided, skipping error reporting."
    exit 1
}

if (-not $JobName) {
    Write-Host "Job name not provided, skipping error reporting."
    exit 1
}

#----------------------------------------------
# Error Extraction Functions
#----------------------------------------------

function Extract-CompilationErrors {
    param (
        [string]$LogPath
    )

    Write-Host "Extracting compilation errors from $LogPath"

    if (-not (Test-Path -Path $LogPath)) {
        Write-Host "Warning: Log path $LogPath does not exist"
        return @()
    }

    # Look for binlog files if it's a compilation job
    $binlogFiles = Get-ChildItem -Path $LogPath -Filter "*.binlog" -Recurse -ErrorAction SilentlyContinue

    $errors = @()

    if ($binlogFiles -and $binlogFiles.Count -gt 0) {
        Write-Host "Found $($binlogFiles.Count) binlog files"

        foreach ($binlog in $binlogFiles) {
            Write-Host "Processing binlog: $($binlog.FullName)"

            try {
                # Use MSBuild binary log reader tool if available
                # This is a simplified approach - in production, consider using MSBuild.StructuredLogger
                $logContent = Get-Content -Path $binlog.FullName -Raw -ErrorAction SilentlyContinue

                if ($logContent) {
                    # Extract error lines
                    $errorMatches = [regex]::Matches($logContent, "(error [A-Za-z]+[0-9]+:.*?(?=error|\z))", [System.Text.RegularExpressions.RegexOptions]::Singleline)

                    foreach ($match in $errorMatches) {
                        $errors += $match.Groups[1].Value.Trim()
                    }
                }
            }
            catch {
                Write-Host "Error processing binlog file $($binlog.FullName): $_"
            }
        }
    } else {
        Write-Host "No binlog files found in $LogPath"
    }

    # Also check for standard text log files with errors
    $textLogFiles = Get-ChildItem -Path $LogPath -Filter "*.log" -Recurse -ErrorAction SilentlyContinue
    if ($textLogFiles -and $textLogFiles.Count -gt 0) {
        Write-Host "Found $($textLogFiles.Count) log files"

        foreach ($logFile in $textLogFiles) {
            try {
                $logContent = Get-Content -Path $logFile.FullName -ErrorAction SilentlyContinue

                if ($logContent) {
                    $errorLines = $logContent | Select-String -Pattern "(error [A-Za-z]+[0-9]+:|Exception:)" -ErrorAction SilentlyContinue

                    foreach ($line in $errorLines) {
                        $errors += $line.Line.Trim()
                    }
                }
            }
            catch {
                Write-Host "Error processing log file $($logFile.FullName): $_"
            }
        }
    } else {
        Write-Host "No text log files found in $LogPath"
    }

    # Also look for MSBuild error output files
    $errorLogFiles = Get-ChildItem -Path $LogPath -Filter "*.err" -Recurse -ErrorAction SilentlyContinue
    if ($errorLogFiles -and $errorLogFiles.Count -gt 0) {
        Write-Host "Found $($errorLogFiles.Count) error files"

        foreach ($errorFile in $errorLogFiles) {
            try {
                $errorContent = Get-Content -Path $errorFile.FullName -ErrorAction SilentlyContinue

                if ($errorContent) {
                    foreach ($line in $errorContent) {
                        if ($line -match "(error [A-Za-z]+[0-9]+:|Exception:)") {
                            $errors += $line.Trim()
                        }
                    }
                }
            }
            catch {
                Write-Host "Error processing error file $($errorFile.FullName): $_"
            }
        }
    }

    return $errors
}

function Extract-TestFailures {
    param (
        [string]$TestResultsPath
    )

    Write-Host "Extracting test failures from $TestResultsPath"

    if (-not (Test-Path -Path $TestResultsPath)) {
        Write-Host "Warning: Test results path $TestResultsPath does not exist"
        return @()
    }

    $failures = @()

    # Look for XML test results
    $testResultFiles = Get-ChildItem -Path $TestResultsPath -Filter "*.xml" -Recurse -ErrorAction SilentlyContinue

    if ($testResultFiles -and $testResultFiles.Count -gt 0) {
        Write-Host "Found $($testResultFiles.Count) test result files"

        foreach ($resultFile in $testResultFiles) {
            try {
                Write-Host "Processing test result file: $($resultFile.FullName)"

                [xml]$testResults = Get-Content -Path $resultFile.FullName -ErrorAction Stop

                # Handle both NUnit and xUnit formats
                # NUnit format
                $nunitFailures = $testResults.SelectNodes("//test-case[@result='Failed']")
                foreach ($testCase in $nunitFailures) {
                    $testName = $testCase.name
                    $message = $testCase.failure.message
                    $stackTrace = $testCase.failure.'stack-trace'

                    $failureText = "Test Failed: $testName"
                    if ($message) { $failureText += "`nError: $message" }
                    if ($stackTrace) { $failureText += "`nStack Trace: $($stackTrace.Split("`n")[0])" } # First line of stack trace

                    $failures += $failureText
                }

                # xUnit format
                $xunitFailures = $testResults.SelectNodes("//test[@result='Fail']")
                foreach ($testCase in $xunitFailures) {
                    $testName = $testCase.name
                    $message = $testCase.failure.message
                    $stackTrace = $testCase.failure.'stack-trace'

                    $failureText = "Test Failed: $testName"
                    if ($message) { $failureText += "`nError: $message" }
                    if ($stackTrace) { $failureText += "`nStack Trace: $($stackTrace.Split("`n")[0])" } # First line of stack trace

                    $failures += $failureText
                }

                # VSTest format
                $vstestFailures = $testResults.SelectNodes("//UnitTestResult[@outcome='Failed']")
                foreach ($testCase in $vstestFailures) {
                    $testName = $testCase.testName
                    $message = $testCase.output.errorInfo.message
                    $stackTrace = $testCase.output.errorInfo.stackTrace

                    $failureText = "Test Failed: $testName"
                    if ($message) { $failureText += "`nError: $message" }
                    if ($stackTrace) { $failureText += "`nStack Trace: $($stackTrace.Split("`n")[0])" } # First line of stack trace

                    $failures += $failureText
                }
            }
            catch {
                Write-Host "Error parsing test result file: $($resultFile.FullName)"
                Write-Host $_.Exception.Message
            }
        }
    } else {
        Write-Host "No test result files found in $TestResultsPath"
    }

    return $failures
}

#----------------------------------------------
# GitHub API Functions
#----------------------------------------------

function Post-GitHubComment {
    param (
        [string]$Owner,
        [string]$Repo,
        [string]$PrNumber,
        [string]$CommentBody,
        [string]$Token
    )

    Write-Host "Posting GitHub comment to PR #$PrNumber"

    # Never log the token
    $headers = @{
        "Authorization" = "token $Token"
        "Accept" = "application/vnd.github.v3+json"
        "User-Agent" = "Azure-DevOps-FSharp-CI"
    }

    $uri = "https://api.github.com/repos/$Owner/$Repo/issues/$PrNumber/comments"
    $body = @{
        "body" = $CommentBody
    } | ConvertTo-Json

    try {
        $response = Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -Body $body -ContentType "application/json" -ErrorAction Stop
        Write-Host "Successfully posted comment to GitHub PR"
        return $response
    }
    catch {
        Write-Host "Error posting comment to GitHub"
        Write-Host "Status code: $($_.Exception.Response.StatusCode.value__)"
        Write-Host "Response: $($_.ErrorDetails.Message)"
        return $null
    }
}

function Update-ExistingComment {
    param (
        [string]$Owner,
        [string]$Repo,
        [string]$PrNumber,
        [string]$CommentId,
        [string]$CommentBody,
        [string]$Token
    )

    Write-Host "Updating existing GitHub comment #$CommentId on PR #$PrNumber"

    $headers = @{
        "Authorization" = "token $Token"
        "Accept" = "application/vnd.github.v3+json"
        "User-Agent" = "Azure-DevOps-FSharp-CI"
    }

    $uri = "https://api.github.com/repos/$Owner/$Repo/issues/comments/$CommentId"
    $body = @{
        "body" = $CommentBody
    } | ConvertTo-Json

    try {
        $response = Invoke-RestMethod -Uri $uri -Method Patch -Headers $headers -Body $body -ContentType "application/json" -ErrorAction Stop
        Write-Host "Successfully updated comment on GitHub PR"
        return $response
    }
    catch {
        Write-Host "Error updating comment on GitHub"
        Write-Host "Status code: $($_.Exception.Response.StatusCode.value__)"
        Write-Host "Response: $($_.ErrorDetails.Message)"
        return $null
    }
}

function Find-ExistingComment {
    param (
        [string]$Owner,
        [string]$Repo,
        [string]$PrNumber,
        [string]$CommentPrefix,
        [string]$Token
    )

    Write-Host "Finding existing comments on PR #$PrNumber"

    $headers = @{
        "Authorization" = "token $Token"
        "Accept" = "application/vnd.github.v3+json"
        "User-Agent" = "Azure-DevOps-FSharp-CI"
    }

    $uri = "https://api.github.com/repos/$Owner/$Repo/issues/$PrNumber/comments"

    try {
        $comments = Invoke-RestMethod -Uri $uri -Method Get -Headers $headers -ErrorAction Stop

        foreach ($comment in $comments) {
            if ($comment.body.StartsWith($CommentPrefix)) {
                Write-Host "Found existing comment from CI: $($comment.id)"
                return $comment.id
            }
        }

        Write-Host "No existing comment found"
        return $null
    }
    catch {
        Write-Host "Error finding existing comments"
        Write-Host "Status code: $($_.Exception.Response.StatusCode.value__)"
        Write-Host "Response: $($_.ErrorDetails.Message)"
        return $null
    }
}

#----------------------------------------------
# Main Logic
#----------------------------------------------

# Define paths based on job type and build configuration
$logsPath = "$BuildSourcesDirectory/artifacts/log"
$testResultsPath = "$BuildSourcesDirectory/artifacts/TestResults"

if ($BuildConfiguration) {
    $logsPath += "/$BuildConfiguration"
    $testResultsPath += "/$BuildConfiguration"
}

Write-Host "Logs path: $logsPath"
Write-Host "Test results path: $testResultsPath"

# Extract errors based on available artifacts
$compilationErrors = @()
$testFailures = @()

if (Test-Path $logsPath) {
    $compilationErrors = Extract-CompilationErrors -LogPath $logsPath
    Write-Host "Found $($compilationErrors.Count) compilation errors"
} else {
    Write-Host "Warning: Log path $logsPath does not exist"
}

if (Test-Path $testResultsPath) {
    $testFailures = Extract-TestFailures -TestResultsPath $testResultsPath
    Write-Host "Found $($testFailures.Count) test failures"
} else {
    Write-Host "Warning: Test results path $testResultsPath does not exist"
}

# Format the comment with error summary
$commentPrefix = "## CI Error Report for $BuildDefinitionName - Job: $JobName"
$commentBody = "$commentPrefix`n`n"
$commentBody += "Build ID: $BuildId`n"
$commentBody += "Job: $JobName`n`n"

if ($compilationErrors.Count -gt 0) {
    $commentBody += "### Compilation Errors`n`n"

    # Limit the number of errors to avoid huge comments
    $maxErrors = [Math]::Min($compilationErrors.Count, 10)

    for ($i = 0; $i -lt $maxErrors; $i++) {
        $commentBody += "```text`n$($compilationErrors[$i])`n````n`n"
    }

    if ($compilationErrors.Count -gt $maxErrors) {
        $commentBody += "_...and $($compilationErrors.Count - $maxErrors) more errors..._`n`n"
    }
}

if ($testFailures.Count -gt 0) {
    $commentBody += "### Test Failures`n`n"

    # Limit the number of failures to avoid huge comments
    $maxFailures = [Math]::Min($testFailures.Count, 10)

    for ($i = 0; $i -lt $maxFailures; $i++) {
        $commentBody += "```text`n$($testFailures[$i])`n````n`n"
    }

    if ($testFailures.Count -gt $maxFailures) {
        $commentBody += "_...and $($testFailures.Count - $maxFailures) more test failures..._`n`n"
    }
}

if ($compilationErrors.Count -eq 0 -and $testFailures.Count -eq 0) {
    Write-Host "No errors or failures found, skipping GitHub comment"
    exit 0
}

# Add a link to the build
$commentBody += "### Build Details`n"

# Create a direct link to the job in Azure DevOps
$orgUrl = $env:SYSTEM_COLLECTIONURI
$projectName = $env:SYSTEM_TEAMPROJECT
$buildDefId = $env:SYSTEM_DEFINITIONID

if ($orgUrl -and $projectName -and $buildDefId -and $BuildId) {
    $buildUrl = "${orgUrl}${projectName}/_build/results?buildId=$BuildId&view=logs&j=$JobName"
    $commentBody += "[View job logs]($buildUrl)`n"
} else {
    $commentBody += "[View complete build log]($env:SYSTEM_TEAMFOUNDATIONSERVERURI$env:SYSTEM_TEAMPROJECT/_build/results?buildId=$BuildId)`n"
}

$commentBody += "`n_This comment was automatically generated by the CI system at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC')_"

try {
    # Check if we already have a comment from our CI for this job
    $existingCommentId = Find-ExistingComment -Owner $GitHubRepoOwner -Repo $GitHubRepoName -PrNumber $PullRequestId -CommentPrefix $commentPrefix -Token $GitHubToken

    if ($existingCommentId) {
        # Update the existing comment
        $result = Update-ExistingComment -Owner $GitHubRepoOwner -Repo $GitHubRepoName -PrNumber $PullRequestId -CommentId $existingCommentId -CommentBody $commentBody -Token $GitHubToken
        if ($result) {
            Write-Host "Successfully updated comment on GitHub PR"
        } else {
            Write-Host "Failed to update comment, attempting to create a new one"
            $result = Post-GitHubComment -Owner $GitHubRepoOwner -Repo $GitHubRepoName -PrNumber $PullRequestId -CommentBody $commentBody -Token $GitHubToken
        }
    } else {
        # Post a new comment
        $result = Post-GitHubComment -Owner $GitHubRepoOwner -Repo $GitHubRepoName -PrNumber $PullRequestId -CommentBody $commentBody -Token $GitHubToken
    }

    if (-not $result) {
        Write-Host "Warning: Failed to post or update GitHub comment"
        exit 1
    }
} catch {
    Write-Host "Exception occurred during GitHub comment posting: $_"
    exit 1
}

Write-Host "Error reporting completed successfully"