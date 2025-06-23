param(
    [Parameter(Mandatory)]
    [string]$BuildId,
    
    [Parameter(Mandatory)]
    [string]$Organization,
    
    [Parameter(Mandatory)]
    [string]$Project,
    
    [Parameter(Mandatory)]
    [string]$AccessToken,
    
    [Parameter(Mandatory)]
    [string]$GitHubToken,
    
    [Parameter(Mandatory)]
    [string]$Repository,
    
    [Parameter(Mandatory)]
    [string]$PullRequestId,
    
    # Testing parameter - when set, script only defines functions but doesn't execute
    [switch]$WhatIf
)

# Set up authentication headers
$azureHeaders = @{
    'Authorization' = "Bearer $AccessToken"
    'Content-Type' = 'application/json'
}

$githubHeaders = @{
    'Authorization' = "token $GitHubToken"
    'Accept' = 'application/vnd.github.v3+json'
    'User-Agent' = 'Azure-DevOps-Build-Reporter'
}

# Job name mappings for user-friendly display
$script:jobMappings = @{
    "WindowsCompressedMetadata" = "Windows Tests (Compressed Metadata)"
    "WindowsCompressedMetadata_Desktop" = "Windows Desktop Tests"
    "WindowsNoRealsig_testCoreclr" = "Windows CoreCLR Tests"
    "WindowsNoRealsig_testDesktop" = "Windows Desktop Tests"
    "WindowsLangVersionPreview" = "Windows Language Version Preview"
    "WindowsStrictIndentation" = "Windows Strict Indentation Tests"
    "WindowsNoStrictIndentation" = "Windows No Strict Indentation Tests"
    "Linux" = "Linux Tests"
    "MacOS" = "macOS Tests"
    "Determinism_Debug" = "Determinism Tests"
    "CheckCodeFormatting" = "Code Formatting Check"
    "EndToEndBuildTests" = "End-to-End Build Tests"
    "Plain_Build_Windows" = "Plain Build (Windows)"
    "Plain_Build_Linux" = "Plain Build (Linux)"
    "Plain_Build_MacOS" = "Plain Build (macOS)"
    "Build_And_Test_AOT_Windows" = "AOT/Trimming Tests"
    "ILVerify" = "IL Verification"
    "Benchmarks" = "Performance Benchmarks"
    "MockOfficial" = "Mock Official Build"
    "Check_Published_Package_Versions" = "Package Version Check"
}

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

function Get-BuildTimeline {
    param($BuildId, $Headers, $Organization, $Project)
    
    $uri = "$Organization$Project/_apis/build/builds/$BuildId/timeline?api-version=7.0"
    Write-Host "📡 Calling: $uri"
    
    try {
        $response = Invoke-RestMethod -Uri $uri -Headers $Headers -Method Get
        $failedJobs = @()
        
        if ($response -and $response.records) {
            foreach ($record in $response.records) {
                # Check for failed jobs/tasks
                if ($record.result -eq "failed" -or $record.result -eq "canceled") {
                    
                    $jobInfo = @{
                        Id = $record.id
                        Name = $record.name
                        Type = $record.type
                        Result = $record.result
                        StartTime = $record.startTime
                        FinishTime = $record.finishTime
                        Issues = $record.issues
                        LogId = $record.log.id
                        ParentId = $record.parentId
                        DisplayName = Get-JobDisplayName -JobName $record.name
                    }
                    
                    # Categorize the failure type
                    switch ($record.type) {
                        "Job" { 
                            $jobInfo.Category = "Job Failure"
                        }
                        "Task" { 
                            $jobInfo.Category = "Task Failure" 
                        }
                        default { 
                            $jobInfo.Category = "Build Failure"
                        }
                    }
                    
                    $failedJobs += $jobInfo
                }
            }
        }
        
        Write-Host "🔍 Found $($failedJobs.Count) failed jobs/tasks"
        return $failedJobs
        
    } catch {
        Write-Warning "Failed to get build timeline: $_"
        return @()
    }
}

function Get-JobDisplayName {
    param($JobName)
    
    # Return mapped name or original name if not found
    return $script:jobMappings[$JobName] ?? $JobName
}

function Get-TestFailures {
    param($BuildId, $Headers, $Organization, $Project)
    
    # Get test runs for this build
    $buildUri = "$Organization$Project/_build/results?buildId=$BuildId"
    $testRunsUri = "$Organization$Project/_apis/test/runs?buildUri=$buildUri&api-version=7.0"
    
    Write-Host "📊 Calling: $testRunsUri"
    
    try {
        $testRuns = Invoke-RestMethod -Uri $testRunsUri -Headers $Headers -Method Get
        $allTestFailures = @()
        
        if ($testRuns -and $testRuns.value) {
            foreach ($run in $testRuns.value) {
                # Check if this test run has failures
                if ($run.unanalyzedTests -gt 0 -or $run.totalTests -gt $run.passedTests) {
                    
                    # Get detailed test results for this run
                    $testResultsUri = "$Organization$Project/_apis/test/runs/$($run.id)/results?api-version=7.0&outcomes=Failed&outcomes=Aborted&outcomes=Timeout"
                    
                    try {
                        $testResults = Invoke-RestMethod -Uri $testResultsUri -Headers $Headers -Method Get
                        
                        if ($testResults -and $testResults.value) {
                            foreach ($result in $testResults.value) {
                                $testFailure = @{
                                    TestName = $result.testCaseTitle
                                    TestMethod = $result.automatedTestName
                                    Outcome = $result.outcome
                                    ErrorMessage = $result.errorMessage
                                    StackTrace = $result.stackTrace
                                    Duration = $result.durationInMs
                                    TestRun = $run.name
                                    JobName = Extract-JobNameFromTestRun -TestRunName $run.name
                                }
                                $allTestFailures += $testFailure
                            }
                        }
                    } catch {
                        Write-Warning "Failed to get test results for run $($run.id): $_"
                    }
                }
            }
        }
        
        Write-Host "🧪 Found $($allTestFailures.Count) test failures"
        return $allTestFailures
        
    } catch {
        Write-Warning "Failed to get test runs: $_"
        return @()
    }
}

function Extract-JobNameFromTestRun {
    param($TestRunName)
    
    # Extract job name from test run names like "WindowsCompressedMetadata testCoreclr"
    if ($TestRunName -match "^([^\s]+)") {
        return $matches[1]
    }
    return $TestRunName
}

function Format-FailureReport {
    param($BuildErrors, $TestFailures, $BuildId, $Organization, $Project)
    
    $report = @{
        HasFailures = ($BuildErrors.Count -gt 0 -or $TestFailures.Count -gt 0)
        BuildErrors = $BuildErrors
        TestFailures = $TestFailures
        BuildUrl = "$Organization$Project/_build/results?buildId=$BuildId"
    }
    
    if (-not $report.HasFailures) {
        return $report
    }
    
    # Generate markdown report
    $markdown = @"
## 🔴 Build/Test Failures Report

"@
    
    # Build Failures Section
    if ($BuildErrors.Count -gt 0) {
        $markdown += @"

### 🏗️ Build Failures ($($BuildErrors.Count))

"@
        
        $groupedErrors = $BuildErrors | Group-Object DisplayName
        foreach ($group in $groupedErrors) {
            $markdown += "**$($group.Name)**`n"
            
            foreach ($error in $group.Group | Select-Object -First 3) {
                $markdown += "- $($error.Category): $($error.Result)"
                if ($error.Issues -and $error.Issues.Count -gt 0) {
                    $firstIssue = $error.Issues[0]
                    if ($firstIssue.message) {
                        $shortMessage = $firstIssue.message.Substring(0, [Math]::Min(100, $firstIssue.message.Length))
                        if ($firstIssue.message.Length -gt 100) { $shortMessage += "..." }
                        $markdown += " - $shortMessage"
                    }
                }
                $markdown += "`n"
            }
            
            if ($group.Count -gt 3) {
                $markdown += "- ... and $($group.Count - 3) more errors`n"
            }
            $markdown += "`n"
        }
    }
    
    # Test Failures Section
    if ($TestFailures.Count -gt 0) {
        $markdown += @"

### 🧪 Test Failures ($($TestFailures.Count))

"@
        
        $groupedTests = $TestFailures | Group-Object JobName
        foreach ($group in $groupedTests) {
            $jobDisplayName = Get-JobDisplayName -JobName $group.Name
            $markdown += "**$jobDisplayName** - $($group.Count) failed tests`n"
            
            foreach ($test in $group.Group | Select-Object -First 5) {
                $markdown += "- ``$($test.TestName)``"
                if ($test.ErrorMessage) {
                    $shortError = $test.ErrorMessage.Substring(0, [Math]::Min(100, $test.ErrorMessage.Length))
                    if ($test.ErrorMessage.Length -gt 100) { $shortError += "..." }
                    $markdown += ": $shortError"
                }
                $markdown += "`n"
            }
            
            if ($group.Count -gt 5) {
                $markdown += "- ... and $($group.Count - 5) more test failures`n"
            }
            $markdown += "`n"
        }
    }
    
    # Summary Section
    $totalJobs = ($BuildErrors | Group-Object DisplayName).Count + ($TestFailures | Group-Object JobName).Count
    $markdown += @"

### 📊 Summary
- **Failed Jobs**: $totalJobs
- **Build Errors**: $($BuildErrors.Count)
- **Test Failures**: $($TestFailures.Count)

[📋 View full build details]($($report.BuildUrl))

---
*This comment was automatically generated by the F# CI pipeline*
"@
    
    $report.MarkdownContent = $markdown
    return $report
}

# TODO: Implement GitHub comment posting
function Post-GitHubComment {
    param($Report, $Repository, $PullRequestId, $Headers)
    
    Write-Host "🚧 GitHub comment posting not yet implemented"
    Write-Host "Would post to: https://github.com/$Repository/pull/$PullRequestId"
    # Implementation will go here
}

# ============================================================================
# MAIN EXECUTION (only run if not in WhatIf mode)
# ============================================================================

if (-not $WhatIf) {
    # Set up authentication headers
    $azureHeaders = @{
        'Authorization' = "Bearer $AccessToken"
        'Content-Type' = 'application/json'
    }

    $githubHeaders = @{
        'Authorization' = "token $GitHubToken"
        'Accept' = 'application/vnd.github.v3+json'
        'User-Agent' = 'Azure-DevOps-Build-Reporter'
    }

    try {
        Write-Host "🔍 Starting failure collection for Build ID: $BuildId"
        
        # Step 1: Get failed jobs from build timeline
        Write-Host "📡 Fetching build timeline..."
        $failedJobs = Get-BuildTimeline -BuildId $BuildId -Headers $azureHeaders -Organization $Organization -Project $Project
        
        # Step 2: Get test failures
        Write-Host "🧪 Fetching test failures..."
        $testFailures = Get-TestFailures -BuildId $BuildId -Headers $azureHeaders -Organization $Organization -Project $Project
        
        # Step 3: Generate failure report
        $failureReport = Format-FailureReport -BuildErrors $failedJobs -TestFailures $testFailures -BuildId $BuildId -Organization $Organization -Project $Project
        
        # Step 4: Post to GitHub PR (placeholder for now)
        if ($failureReport.HasFailures) {
            Write-Host "📝 Failure report generated:"
            Write-Host $failureReport.MarkdownContent
            Write-Host ""
            Write-Host "⚠️ GitHub comment posting not yet implemented"
            # TODO: Post-GitHubComment -Report $failureReport -Repository $Repository -PullRequestId $PullRequestId -Headers $githubHeaders
        } else {
            Write-Host "✅ No failures found to report"
        }
        
    } catch {
        Write-Error "❌ Failed to process build failures: $_"
        exit 1
    }
}
