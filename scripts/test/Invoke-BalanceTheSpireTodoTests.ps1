[CmdletBinding()]
param(
    [string]$BaseUrl = "http://127.0.0.1:51237",
    [string]$Seed = "BALANCE_TODO_TEST",
    [string]$Encounter = "SLUMBERING_BEETLE_NORMAL",
    [string]$OutputPath,
    [string]$LogPath,
    [string[]]$Only,
    [switch]$StopOnFailure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:Results = New-Object System.Collections.Generic.List[object]
$script:ResolvedLogPath = $null

function Write-LogLine {
    param([Parameter(Mandatory = $true)][string]$Message)

    $timestamp = Get-Date -Format "s"
    $line = "[{0}] {1}" -f $timestamp, $Message
    Write-Host $line

    if ($script:ResolvedLogPath) {
        Add-Content -LiteralPath $script:ResolvedLogPath -Value $line -Encoding UTF8
    }
}

function Invoke-AgentApi {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("GET", "POST")]
        [string]$Method,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [object]$Body
    )

    $uri = "$BaseUrl$Path"
    $invokeParams = @{
        Uri         = $uri
        Method      = $Method
        ErrorAction = "Stop"
    }

    if ($null -ne $Body) {
        $invokeParams.ContentType = "application/json"
        $invokeParams.Body = $Body | ConvertTo-Json -Depth 20 -Compress
    }

    $response = Invoke-RestMethod @invokeParams
    if (-not $response.ok) {
        $message = if ($response.error.message) { $response.error.message } else { "Unknown API error." }
        $details = if ($response.error.details) { " $($response.error.details)" } else { "" }
        throw "API $Method $Path failed: $message$details"
    }

    return $response.data
}

function Wait-Until {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Condition,

        [int]$TimeoutMs = 10000,
        [int]$PollMs = 100,
        [string]$Message = "Condition was not met in time."
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) {
            return
        }

        Start-Sleep -Milliseconds $PollMs
    }

    throw $Message
}

function Get-State {
    Invoke-AgentApi -Method GET -Path "/state"
}

function Get-Hand {
    Get-Pile -Pile "Hand"
}

function Get-PileCardCount {
    param([Parameter(Mandatory = $true)][object]$PileSnapshot)
    return [int]$PileSnapshot.PSObject.Properties["count"].Value
}

function Get-Pile {
    param([Parameter(Mandatory = $true)][string]$Pile)
    Invoke-AgentApi -Method POST -Path "/cards/pile" -Body @{ pile = $Pile }
}

function Spawn-Card {
    param(
        [Parameter(Mandatory = $true)][string]$CardId,
        [string]$Pile = "Hand",
        [int]$UpgradeCount = 0,
        [string]$Position = "Bottom",
        [int]$Count = 1
    )

    Invoke-AgentApi -Method POST -Path "/cards/spawn" -Body @{
        cardId       = $CardId
        pile         = $Pile
        upgradeCount = $UpgradeCount
        position     = $Position
        count        = $Count
    }
}

function Draw-Cards {
    param([int]$Count = 1)
    Invoke-AgentApi -Method POST -Path "/cards/draw" -Body @{ count = $Count }
}

function Play-Card {
    param(
        [int]$HandIndex,
        [int]$EnemyIndex = -1,
        [switch]$TargetSelf,
        [string[]]$SelectionCardIds,
        [int[]]$SelectionIndexes
    )

    $body = @{
        handIndex = $HandIndex
        timeoutMs = 15000
    }

    if ($TargetSelf) {
        $body.targetSelf = $true
    }
    elseif ($EnemyIndex -ge 0) {
        $body.enemyIndex = $EnemyIndex
    }

    if ($SelectionCardIds) {
        $body.selectionCardIds = $SelectionCardIds
    }

    if ($SelectionIndexes) {
        $body.selectionIndexes = $SelectionIndexes
    }

    Invoke-AgentApi -Method POST -Path "/cards/play" -Body $body
}

function Use-Console {
    param([Parameter(Mandatory = $true)][string]$Command)
    Invoke-AgentApi -Method POST -Path "/console" -Body @{ command = $Command } | Out-Null
}

function End-Turn {
    Invoke-AgentApi -Method POST -Path "/combat/end-turn" -Body @{}
}

function Get-LocalPlayerCreature {
    param([object]$State)
    return @($State.combat.players | Where-Object { $_.isLocalPlayer })[0]
}

function Get-EnemyCreature {
    param(
        [object]$State,
        [int]$Index = 0
    )

    return @($State.combat.enemies)[$Index]
}

function Get-FirstMatchingCard {
    param(
        [object[]]$Cards,
        [string]$CardId,
        [object]$IsUpgraded = $null,
        [object]$Damage = $null,
        [object]$EnergyCostCurrent = $null
    )

    $matches = @($Cards | Where-Object {
        if ($_.id -ne $CardId) {
            return $false
        }

        if ($null -ne $IsUpgraded -and [bool]$_.isUpgraded -ne [bool]$IsUpgraded) {
            return $false
        }

        if ($null -ne $Damage -and (Get-DynamicIntValue -Card $_ -Key "Damage") -ne [int]$Damage) {
            return $false
        }

        if ($null -ne $EnergyCostCurrent -and [int]$_.energyCostCurrent -ne [int]$EnergyCostCurrent) {
            return $false
        }

        return $true
    })

    if ($matches.Count -eq 0) {
        return $null
    }

    return $matches[0]
}

function Get-DynamicIntValue {
    param(
        [Parameter(Mandatory = $true)][object]$Card,
        [Parameter(Mandatory = $true)][string]$Key
    )

    $dynamicVars = $Card.dynamicVars
    if ($null -eq $dynamicVars) {
        throw "Card [$($Card.id)] is missing dynamicVars."
    }

    $property = $dynamicVars.PSObject.Properties[$Key]
    if ($null -eq $property) {
        throw "Card [$($Card.id)] is missing dynamic var [$Key]."
    }

    return [int]$property.Value.intValue
}

function Find-Power {
    param(
        [Parameter(Mandatory = $true)][object]$Creature,
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    $powers = @($Creature.powers)
    $exact = @($powers | Where-Object { $_.id -eq $Pattern })
    if ($exact.Count -gt 0) {
        return $exact[0]
    }

    $fuzzy = @($powers | Where-Object { $_.id -like "*$Pattern*" })
    if ($fuzzy.Count -gt 0) {
        return $fuzzy[0]
    }

    return $null
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message Expected [$Expected], got [$Actual]."
    }
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Start-IsolatedCombat {
    param(
        [int]$BonusEnergy = 10,
        [int]$BonusStars = 10
    )

    Invoke-AgentApi -Method POST -Path "/run/start" -Body @{
        character       = "Regent"
        seed            = $Seed
        ascensionLevel  = 0
        resetToMainMenu = $true
        shouldSave      = $false
    } | Out-Null

    Invoke-AgentApi -Method POST -Path "/fight" -Body @{ encounter = $Encounter } | Out-Null

    Wait-Until -TimeoutMs 15000 -PollMs 100 -Message "Combat did not reach a playable player turn." -Condition {
        $state = Get-State
        return $null -ne $state.combat -and
            $state.combat.isInProgress -and
            $state.combat.currentSide -eq "Player" -and
            -not $state.combat.playerActionsDisabled
    }

    Wait-Until -TimeoutMs 15000 -PollMs 100 -Message "Opening hand did not finish drawing." -Condition {
        $hand = Get-Hand
        return (Get-PileCardCount -PileSnapshot $hand) -ge 5
    }

    if ($BonusEnergy -ne 0) {
        Use-Console ("energy {0}" -f $BonusEnergy)
    }

    if ($BonusStars -ne 0) {
        Use-Console ("stars {0}" -f $BonusStars)
    }
}

function Wait-For-PlayerTurn {
    Wait-Until -TimeoutMs 15000 -PollMs 100 -Message "Player turn did not become ready." -Condition {
        $state = Get-State
        return $null -ne $state.combat -and
            $state.combat.isInProgress -and
            $state.combat.currentSide -eq "Player" -and
            -not $state.combat.playerActionsDisabled
    }
}

function Wait-For-EnemyTurn {
    Wait-Until -TimeoutMs 10000 -PollMs 50 -Message "Enemy turn did not begin." -Condition {
        $state = Get-State
        return $null -ne $state.combat -and
            $state.combat.isInProgress -and
            $state.combat.currentSide -eq "Enemy"
    }
}

function Invoke-TestCase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Body
    )

    Write-LogLine "[TEST] $Name"

    try {
        $detail = & $Body
        if ([string]::IsNullOrWhiteSpace([string]$detail)) {
            $detail = "Passed."
        }

        $script:Results.Add([pscustomobject]@{
            Name   = $Name
            Passed = $true
            Detail = [string]$detail
        })

        Write-LogLine "  PASS $detail"
    }
    catch {
        $message = $_.Exception.Message
        $script:Results.Add([pscustomobject]@{
            Name   = $Name
            Passed = $false
            Detail = $message
        })

        Write-LogLine "  FAIL $message"

        if ($StopOnFailure) {
            throw
        }
    }
}

function Save-Results {
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $artifactDir = Join-Path $PSScriptRoot "..\..\artifacts\test-results"
        $artifactDir = [System.IO.Path]::GetFullPath($artifactDir)
        New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null
        $scriptName = "BalanceTheSpireTodoTests-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date)
        $script:ResolvedOutputPath = Join-Path $artifactDir $scriptName
    }
    else {
        $parent = Split-Path -Parent $OutputPath
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }

        $script:ResolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    }

    [pscustomobject]@{
        baseUrl   = $BaseUrl
        seed      = $Seed
        encounter = $Encounter
        generated = (Get-Date).ToString("s")
        results   = $script:Results
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $script:ResolvedOutputPath -Encoding UTF8
}

function Resolve-LogPath {
    if ([string]::IsNullOrWhiteSpace($LogPath)) {
        $artifactDir = Join-Path $PSScriptRoot "..\..\artifacts\test-results"
        $artifactDir = [System.IO.Path]::GetFullPath($artifactDir)
        New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null
        $logName = "BalanceTheSpireTodoTests-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date)
        $script:ResolvedLogPath = Join-Path $artifactDir $logName
    }
    else {
        $parent = Split-Path -Parent $LogPath
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }

        $script:ResolvedLogPath = [System.IO.Path]::GetFullPath($LogPath)
    }

    Set-Content -LiteralPath $script:ResolvedLogPath -Value @(
        "BaseUrl=$BaseUrl"
        "Seed=$Seed"
        "Encounter=$Encounter"
        "Started=$(Get-Date -Format s)"
        ""
    ) -Encoding UTF8
}

$tests = @(
    @{
        Name = "SpoilsOfBattle"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "SPOILS_OF_BATTLE"
            $card = @($spawn.cards)[0]
            $expectedForge = 5
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Forge") -Expected $expectedForge -Message "SpoilsOfBattle forge was wrong."

            $beforeHand = Get-Hand
            $beforeHandCount = Get-PileCardCount -PileSnapshot $beforeHand
            $play = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            Assert-Equal -Actual (Get-DynamicIntValue -Card $play.cardBefore -Key "Forge") -Expected $expectedForge -Message "SpoilsOfBattle cardBefore forge was wrong."

            $hand = Get-Hand
            $blade = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "SOVEREIGN_BLADE"
            Assert-True -Condition ($null -ne $blade) -Message "SpoilsOfBattle did not create SovereignBlade."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $blade -Key "Damage") -Expected 15 -Message "SpoilsOfBattle forged SovereignBlade damage was wrong."
            Assert-Equal -Actual (Get-PileCardCount -PileSnapshot $hand) -Expected ($beforeHandCount + 2) -Message "SpoilsOfBattle hand count delta was wrong."

            return "Forge=5, drew 2 cards, created a 15-damage SovereignBlade."
        }
    },
    @{
        Name = "SpoilsOfBattle+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "SPOILS_OF_BATTLE" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Forge") -Expected 8 -Message "SpoilsOfBattle+ forge was wrong."

            $beforeHand = Get-Hand
            $beforeHandCount = Get-PileCardCount -PileSnapshot $beforeHand
            $play = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            Assert-Equal -Actual (Get-DynamicIntValue -Card $play.cardBefore -Key "Forge") -Expected 8 -Message "SpoilsOfBattle+ cardBefore forge was wrong."

            $hand = Get-Hand
            $blade = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "SOVEREIGN_BLADE"
            Assert-True -Condition ($null -ne $blade) -Message "SpoilsOfBattle+ did not create SovereignBlade."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $blade -Key "Damage") -Expected 18 -Message "SpoilsOfBattle+ forged SovereignBlade damage was wrong."
            Assert-Equal -Actual (Get-PileCardCount -PileSnapshot $hand) -Expected ($beforeHandCount + 2) -Message "SpoilsOfBattle+ hand count delta was wrong."

            return "Forge=8, drew 2 cards, created an 18-damage SovereignBlade."
        }
    },
    @{
        Name = "Arsenal"
        Body = {
            Start-IsolatedCombat
            $arsenalSpawn = Spawn-Card -CardId "ARSENAL"
            $arsenal = @($arsenalSpawn.cards)[0]
            $playArsenal = Play-Card -HandIndex ([int]$arsenal.handIndex) -TargetSelf

            $afterArsenal = Get-State
            $player = Get-LocalPlayerCreature -State $afterArsenal
            $arsenalPower = Find-Power -Creature $player -Pattern "ARSENAL_POWER"
            Assert-True -Condition ($null -ne $arsenalPower) -Message "Arsenal power was not applied."
            Assert-Equal -Actual ([int]$arsenalPower.amount) -Expected 1 -Message "Arsenal power amount was wrong."

            $bundleSpawn = Spawn-Card -CardId "BUNDLE_OF_JOY"
            $bundle = @($bundleSpawn.cards)[0]
            $null = Play-Card -HandIndex ([int]$bundle.handIndex) -TargetSelf

            $afterBundle = Get-State
            $playerAfterBundle = Get-LocalPlayerCreature -State $afterBundle
            $strength = Find-Power -Creature $playerAfterBundle -Pattern "STRENGTH_POWER"
            Assert-True -Condition ($null -ne $strength) -Message "Arsenal did not grant strength after generated cards."
            Assert-Equal -Actual ([int]$strength.amount) -Expected 3 -Message "Arsenal strength gained after BundleOfJoy was wrong."

            return "ArsenalPower applied and 3 generated cards granted 3 Strength."
        }
    },
    @{
        Name = "Arsenal+"
        Body = {
            Start-IsolatedCombat
            $null = Spawn-Card -CardId "ARSENAL" -UpgradeCount 1
            $hand = Get-Hand
            $arsenal = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "ARSENAL" -IsUpgraded ([bool]$true)
            Assert-True -Condition ($null -ne $arsenal) -Message "Arsenal+ was not found in hand."
            Assert-True -Condition ($null -ne $arsenal.description -and $arsenal.description.StartsWith("[gold]")) -Message "Arsenal+ description did not show the upgraded Innate line."

            $null = Play-Card -HandIndex ([int]$arsenal.handIndex) -TargetSelf

            $bundleSpawn = Spawn-Card -CardId "BUNDLE_OF_JOY"
            $bundle = @($bundleSpawn.cards)[0]
            $null = Play-Card -HandIndex ([int]$bundle.handIndex) -TargetSelf

            $state = Get-State
            $player = Get-LocalPlayerCreature -State $state
            $strength = Find-Power -Creature $player -Pattern "STRENGTH_POWER"
            Assert-True -Condition ($null -ne $strength) -Message "Arsenal+ did not grant strength after generated cards."
            Assert-Equal -Actual ([int]$strength.amount) -Expected 3 -Message "Arsenal+ strength gained after BundleOfJoy was wrong."

            return "Description shows Innate, and generated cards still granted 3 Strength."
        }
    },
    @{
        Name = "Charge"
        Body = {
            Start-IsolatedCombat
            $null = Spawn-Card -CardId "GLITTERSTREAM" -Pile "Draw" -Position "Top"
            $null = Spawn-Card -CardId "SOLAR_STRIKE" -Pile "Draw" -Position "Top"
            $chargeSpawn = Spawn-Card -CardId "CHARGE"
            $charge = @($chargeSpawn.cards)[0]
            $null = Play-Card -HandIndex ([int]$charge.handIndex) -TargetSelf -SelectionCardIds @("GLITTERSTREAM", "SOLAR_STRIKE")

            $draw = Get-Pile -Pile "Draw"
            Assert-True -Condition ($null -eq (Get-FirstMatchingCard -Cards @($draw.cards) -CardId "GLITTERSTREAM")) -Message "Charge did not transform GLITTERSTREAM."
            Assert-True -Condition ($null -eq (Get-FirstMatchingCard -Cards @($draw.cards) -CardId "SOLAR_STRIKE")) -Message "Charge did not transform SOLAR_STRIKE."
            $diveBombs = @($draw.cards | Where-Object { $_.id -eq "MINION_DIVE_BOMB" -and -not $_.isUpgraded })
            Assert-Equal -Actual $diveBombs.Count -Expected 2 -Message "Charge did not create 2 base MinionDiveBomb cards."

            return "Two chosen draw-pile cards transformed into MinionDiveBomb."
        }
    },
    @{
        Name = "Charge+"
        Body = {
            Start-IsolatedCombat
            $null = Spawn-Card -CardId "GLITTERSTREAM" -Pile "Draw" -Position "Top"
            $null = Spawn-Card -CardId "SOLAR_STRIKE" -Pile "Draw" -Position "Top"
            $chargeSpawn = Spawn-Card -CardId "CHARGE" -UpgradeCount 1
            $charge = @($chargeSpawn.cards)[0]
            $null = Play-Card -HandIndex ([int]$charge.handIndex) -TargetSelf -SelectionCardIds @("GLITTERSTREAM", "SOLAR_STRIKE")

            $draw = Get-Pile -Pile "Draw"
            $diveBombs = @($draw.cards | Where-Object { $_.id -eq "MINION_DIVE_BOMB" -and $_.isUpgraded })
            Assert-Equal -Actual $diveBombs.Count -Expected 2 -Message "Charge+ did not create 2 upgraded MinionDiveBomb cards."

            return "Two chosen draw-pile cards transformed into MinionDiveBomb+."
        }
    },
    @{
        Name = "Begone"
        Body = {
            Start-IsolatedCombat
            $null = Spawn-Card -CardId "GLITTERSTREAM"
            $begoneSpawn = Spawn-Card -CardId "BEGONE"
            $begone = @($begoneSpawn.cards)[0]
            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0

            $null = Play-Card -HandIndex ([int]$begone.handIndex) -EnemyIndex 0 -SelectionCardIds @("GLITTERSTREAM")

            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 4 -Message "Begone damage was wrong."

            $hand = Get-Hand
            Assert-True -Condition ($null -eq (Get-FirstMatchingCard -Cards @($hand.cards) -CardId "GLITTERSTREAM")) -Message "Begone did not replace the selected card."
            $minionStrike = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "MINION_STRIKE" -IsUpgraded ([bool]$false)
            Assert-True -Condition ($null -ne $minionStrike) -Message "Begone did not create MinionStrike."

            return "Selected hand card transformed into MinionStrike after dealing 4 damage."
        }
    },
    @{
        Name = "Begone+"
        Body = {
            Start-IsolatedCombat
            $null = Spawn-Card -CardId "GLITTERSTREAM"
            $begoneSpawn = Spawn-Card -CardId "BEGONE" -UpgradeCount 1
            $begone = @($begoneSpawn.cards)[0]
            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0

            $null = Play-Card -HandIndex ([int]$begone.handIndex) -EnemyIndex 0 -SelectionCardIds @("GLITTERSTREAM")

            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 5 -Message "Begone+ damage was wrong."

            $hand = Get-Hand
            $minionStrike = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "MINION_STRIKE" -IsUpgraded ([bool]$true)
            Assert-True -Condition ($null -ne $minionStrike) -Message "Begone+ did not create MinionStrike+."

            return "Selected hand card transformed into MinionStrike+ after dealing 5 damage."
        }
    },
    @{
        Name = "MinionDiveBomb"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "MINION_DIVE_BOMB"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual ([int]$card.energyCostCurrent) -Expected 0 -Message "MinionDiveBomb cost was wrong."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Damage") -Expected 13 -Message "MinionDiveBomb damage was wrong."

            $beforeState = Get-State
            $beforeEnergy = [int]@($beforeState.run.players | Where-Object { $_.isLocalPlayer })[0].energy
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 0
            $afterState = Get-State
            $afterEnergy = [int]@($afterState.run.players | Where-Object { $_.isLocalPlayer })[0].energy
            Assert-Equal -Actual $afterEnergy -Expected $beforeEnergy -Message "MinionDiveBomb should cost 0 energy when played."

            return "Energy cost stayed at 0 when played."
        }
    },
    @{
        Name = "MinionDiveBomb+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "MINION_DIVE_BOMB" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual ([int]$card.energyCostCurrent) -Expected 0 -Message "MinionDiveBomb+ cost was wrong."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Damage") -Expected 16 -Message "MinionDiveBomb+ damage was wrong."

            $beforeState = Get-State
            $beforeEnergy = [int]@($beforeState.run.players | Where-Object { $_.isLocalPlayer })[0].energy
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 0
            $afterState = Get-State
            $afterEnergy = [int]@($afterState.run.players | Where-Object { $_.isLocalPlayer })[0].energy
            Assert-Equal -Actual $afterEnergy -Expected $beforeEnergy -Message "MinionDiveBomb+ should cost 0 energy when played."

            return "Upgraded card still costs 0 energy and deals 16."
        }
    },
    @{
        Name = "CollisionCourse"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "COLLISION_COURSE"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Damage") -Expected 11 -Message "CollisionCourse damage was wrong."

            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 0
            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 11 -Message "CollisionCourse damage dealt was wrong."

            $hand = Get-Hand
            $debris = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "DEBRIS"
            Assert-True -Condition ($null -ne $debris) -Message "CollisionCourse did not generate Debris."

            return "Dealt 11 damage and generated Debris."
        }
    },
    @{
        Name = "CollisionCourse+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "COLLISION_COURSE" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Damage") -Expected 15 -Message "CollisionCourse+ damage was wrong."

            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 0
            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 15 -Message "CollisionCourse+ damage dealt was wrong."

            $hand = Get-Hand
            $debris = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "DEBRIS"
            Assert-True -Condition ($null -ne $debris) -Message "CollisionCourse+ did not generate Debris."

            return "Dealt 15 damage and generated Debris."
        }
    },
    @{
        Name = "HeirloomHammer"
        Body = {
            Start-IsolatedCombat
            $null = Spawn-Card -CardId "FLASH_OF_STEEL"
            $hammerSpawn = Spawn-Card -CardId "HEIRLOOM_HAMMER"
            $hammer = @($hammerSpawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $hammer -Key "Damage") -Expected 22 -Message "HeirloomHammer damage was wrong."

            $beforeHand = Get-Hand
            $beforeFlashCount = @($beforeHand.cards | Where-Object { $_.id -eq "FLASH_OF_STEEL" }).Count
            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0

            $null = Play-Card -HandIndex ([int]$hammer.handIndex) -EnemyIndex 0 -SelectionCardIds @("FLASH_OF_STEEL")

            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 22 -Message "HeirloomHammer damage dealt was wrong."

            $afterHand = Get-Hand
            $afterFlashCount = @($afterHand.cards | Where-Object { $_.id -eq "FLASH_OF_STEEL" }).Count
            Assert-Equal -Actual $afterFlashCount -Expected ($beforeFlashCount + 1) -Message "HeirloomHammer did not duplicate the selected colorless card."

            return "Dealt 22 damage and duplicated the chosen colorless card."
        }
    },
    @{
        Name = "HeirloomHammer+"
        Body = {
            Start-IsolatedCombat
            $null = Spawn-Card -CardId "FLASH_OF_STEEL"
            $hammerSpawn = Spawn-Card -CardId "HEIRLOOM_HAMMER" -UpgradeCount 1
            $hammer = @($hammerSpawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $hammer -Key "Damage") -Expected 25 -Message "HeirloomHammer+ damage was wrong."

            $beforeHand = Get-Hand
            $beforeFlashCount = @($beforeHand.cards | Where-Object { $_.id -eq "FLASH_OF_STEEL" }).Count
            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0

            $null = Play-Card -HandIndex ([int]$hammer.handIndex) -EnemyIndex 0 -SelectionCardIds @("FLASH_OF_STEEL")

            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 25 -Message "HeirloomHammer+ damage dealt was wrong."

            $afterHand = Get-Hand
            $afterFlashCount = @($afterHand.cards | Where-Object { $_.id -eq "FLASH_OF_STEEL" }).Count
            Assert-Equal -Actual $afterFlashCount -Expected ($beforeFlashCount + 1) -Message "HeirloomHammer+ did not duplicate the selected colorless card."

            return "Dealt 25 damage and duplicated the chosen colorless card."
        }
    },
    @{
        Name = "GatherLight"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "GATHER_LIGHT"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Block") -Expected 8 -Message "GatherLight block was wrong."

            $beforeState = Get-State
            $playerBefore = Get-LocalPlayerCreature -State $beforeState
            $starsBefore = [int]@($beforeState.run.players | Where-Object { $_.isLocalPlayer })[0].stars
            $null = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            $afterState = Get-State
            $playerAfter = Get-LocalPlayerCreature -State $afterState
            $starsAfter = [int]@($afterState.run.players | Where-Object { $_.isLocalPlayer })[0].stars

            Assert-Equal -Actual ([int]$playerAfter.block - [int]$playerBefore.block) -Expected 8 -Message "GatherLight block gained was wrong."
            Assert-Equal -Actual ($starsAfter - $starsBefore) -Expected 1 -Message "GatherLight stars gained was wrong."

            return "Gained 8 block and 1 star."
        }
    },
    @{
        Name = "GatherLight+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "GATHER_LIGHT" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Block") -Expected 11 -Message "GatherLight+ block was wrong."

            $beforeState = Get-State
            $playerBefore = Get-LocalPlayerCreature -State $beforeState
            $starsBefore = [int]@($beforeState.run.players | Where-Object { $_.isLocalPlayer })[0].stars
            $null = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            $afterState = Get-State
            $playerAfter = Get-LocalPlayerCreature -State $afterState
            $starsAfter = [int]@($afterState.run.players | Where-Object { $_.isLocalPlayer })[0].stars

            Assert-Equal -Actual ([int]$playerAfter.block - [int]$playerBefore.block) -Expected 11 -Message "GatherLight+ block gained was wrong."
            Assert-Equal -Actual ($starsAfter - $starsBefore) -Expected 1 -Message "GatherLight+ stars gained was wrong."

            return "Gained 11 block and still only 1 star."
        }
    },
    @{
        Name = "BundleOfJoy"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "BUNDLE_OF_JOY"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual ([int]$card.energyCostCurrent) -Expected 1 -Message "BundleOfJoy cost was wrong."

            $beforeHand = Get-Hand
            $beforeHandCount = Get-PileCardCount -PileSnapshot $beforeHand
            $null = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            $afterHand = Get-Hand
            $afterHandCount = Get-PileCardCount -PileSnapshot $afterHand
            Assert-Equal -Actual ($afterHandCount - $beforeHandCount) -Expected 2 -Message "BundleOfJoy net hand delta was wrong."

            return "Cost 1, and playing it produced a net +2 cards."
        }
    },
    @{
        Name = "BundleOfJoy+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "BUNDLE_OF_JOY" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual ([int]$card.energyCostCurrent) -Expected 1 -Message "BundleOfJoy+ cost was wrong."

            $beforeHand = Get-Hand
            $beforeHandCount = Get-PileCardCount -PileSnapshot $beforeHand
            $null = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            $afterHand = Get-Hand
            $afterHandCount = Get-PileCardCount -PileSnapshot $afterHand
            Assert-Equal -Actual ($afterHandCount - $beforeHandCount) -Expected 3 -Message "BundleOfJoy+ net hand delta was wrong."

            return "Cost 1, and playing it produced a net +3 cards."
        }
    },
    @{
        Name = "IAmInvincible"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "I_AM_INVINCIBLE" -Pile "Draw" -Position "Top"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Block") -Expected 10 -Message "IAmInvincible block was wrong."

            $null = End-Turn
            Wait-For-EnemyTurn
            Wait-Until -TimeoutMs 5000 -PollMs 50 -Message "IAmInvincible did not leave the draw pile." -Condition {
                $draw = Get-Pile -Pile "Draw"
                return $null -eq (Get-FirstMatchingCard -Cards @($draw.cards) -CardId "I_AM_INVINCIBLE")
            }

            $discard = Get-Pile -Pile "Discard"
            $playedCard = Get-FirstMatchingCard -Cards @($discard.cards) -CardId "I_AM_INVINCIBLE"
            Assert-True -Condition ($null -ne $playedCard) -Message "IAmInvincible did not auto-play into discard."

            return "Left the draw pile at end turn and auto-played into discard."
        }
    },
    @{
        Name = "IAmInvincible+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "I_AM_INVINCIBLE" -UpgradeCount 1 -Pile "Draw" -Position "Top"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Block") -Expected 13 -Message "IAmInvincible+ block was wrong."

            $null = End-Turn
            Wait-For-EnemyTurn
            Wait-Until -TimeoutMs 5000 -PollMs 50 -Message "IAmInvincible+ did not leave the draw pile." -Condition {
                $draw = Get-Pile -Pile "Draw"
                return $null -eq (Get-FirstMatchingCard -Cards @($draw.cards) -CardId "I_AM_INVINCIBLE")
            }

            $discard = Get-Pile -Pile "Discard"
            $playedCard = Get-FirstMatchingCard -Cards @($discard.cards) -CardId "I_AM_INVINCIBLE" -IsUpgraded ([bool]$true)
            Assert-True -Condition ($null -ne $playedCard) -Message "IAmInvincible+ did not auto-play into discard."

            return "Upgraded card also auto-played from the top of the draw pile."
        }
    },
    @{
        Name = "KinglyKick"
        Body = {
            Start-IsolatedCombat
            $null = Spawn-Card -CardId "KINGLY_KICK" -Pile "Draw" -Position "Top"
            $draw = Draw-Cards -Count 1
            $card = Get-FirstMatchingCard -Cards @($draw.cards) -CardId "KINGLY_KICK" -EnergyCostCurrent 3
            Assert-True -Condition ($null -ne $card) -Message "KinglyKick was not drawn with reduced cost."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Damage") -Expected 27 -Message "KinglyKick damage after draw was wrong."

            return "Drawing the card reduced its combat cost to 3 while keeping damage at 27."
        }
    },
    @{
        Name = "KinglyKick+"
        Body = {
            Start-IsolatedCombat
            $null = Spawn-Card -CardId "KINGLY_KICK" -UpgradeCount 1 -Pile "Draw" -Position "Top"
            $draw = Draw-Cards -Count 1
            $card = Get-FirstMatchingCard -Cards @($draw.cards) -CardId "KINGLY_KICK" -IsUpgraded ([bool]$true) -EnergyCostCurrent 3
            Assert-True -Condition ($null -ne $card) -Message "KinglyKick+ was not drawn with reduced cost."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Damage") -Expected 35 -Message "KinglyKick+ damage after draw was wrong."

            return "Drawing the upgraded card reduced its combat cost to 3 while keeping damage at 35."
        }
    },
    @{
        Name = "KinglyPunch"
        Body = {
            Start-IsolatedCombat
            $null = Spawn-Card -CardId "KINGLY_PUNCH" -Pile "Draw" -Position "Top"
            $draw = Draw-Cards -Count 1
            $card = Get-FirstMatchingCard -Cards @($draw.cards) -CardId "KINGLY_PUNCH" -Damage 12
            Assert-True -Condition ($null -ne $card) -Message "KinglyPunch did not gain damage when drawn."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Increase") -Expected 4 -Message "KinglyPunch Increase value was wrong."

            return "Drawing the card increased its damage from 8 to 12."
        }
    },
    @{
        Name = "KinglyPunch+"
        Body = {
            Start-IsolatedCombat
            $null = Spawn-Card -CardId "KINGLY_PUNCH" -UpgradeCount 1 -Pile "Draw" -Position "Top"
            $draw = Draw-Cards -Count 1
            $card = Get-FirstMatchingCard -Cards @($draw.cards) -CardId "KINGLY_PUNCH" -IsUpgraded ([bool]$true) -Damage 16
            Assert-True -Condition ($null -ne $card) -Message "KinglyPunch+ did not gain damage when drawn."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Increase") -Expected 6 -Message "KinglyPunch+ Increase value was wrong."

            return "Drawing the upgraded card increased its damage from 10 to 16."
        }
    },
    @{
        Name = "SolarStrike"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "SOLAR_STRIKE"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Damage") -Expected 9 -Message "SolarStrike damage was wrong."

            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0
            $starsBefore = [int]@($beforeState.run.players | Where-Object { $_.isLocalPlayer })[0].stars
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 0
            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            $starsAfter = [int]@($afterState.run.players | Where-Object { $_.isLocalPlayer })[0].stars

            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 9 -Message "SolarStrike damage dealt was wrong."
            Assert-Equal -Actual ($starsAfter - $starsBefore) -Expected 1 -Message "SolarStrike stars gained was wrong."

            return "Dealt 9 damage and still granted 1 star."
        }
    },
    @{
        Name = "SolarStrike+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "SOLAR_STRIKE" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Damage") -Expected 10 -Message "SolarStrike+ damage was wrong."

            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0
            $starsBefore = [int]@($beforeState.run.players | Where-Object { $_.isLocalPlayer })[0].stars
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 0
            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            $starsAfter = [int]@($afterState.run.players | Where-Object { $_.isLocalPlayer })[0].stars

            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 10 -Message "SolarStrike+ damage dealt was wrong."
            Assert-Equal -Actual ($starsAfter - $starsBefore) -Expected 1 -Message "SolarStrike+ stars gained was wrong."

            return "Dealt 10 damage and upgrade no longer added extra stars."
        }
    },
    @{
        Name = "Patter"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "PATTER"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Block") -Expected 9 -Message "Patter block was wrong."

            $beforeState = Get-State
            $playerBefore = Get-LocalPlayerCreature -State $beforeState
            $null = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            $afterState = Get-State
            $playerAfter = Get-LocalPlayerCreature -State $afterState
            $vigor = Find-Power -Creature $playerAfter -Pattern "VIGOR_POWER"

            Assert-Equal -Actual ([int]$playerAfter.block - [int]$playerBefore.block) -Expected 9 -Message "Patter block gained was wrong."
            Assert-True -Condition ($null -ne $vigor) -Message "Patter did not apply Vigor."
            Assert-Equal -Actual ([int]$vigor.amount) -Expected 2 -Message "Patter Vigor amount was wrong."

            return "Granted 9 block and 2 Vigor."
        }
    },
    @{
        Name = "Patter+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "PATTER" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Block") -Expected 11 -Message "Patter+ block was wrong."

            $beforeState = Get-State
            $playerBefore = Get-LocalPlayerCreature -State $beforeState
            $null = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            $afterState = Get-State
            $playerAfter = Get-LocalPlayerCreature -State $afterState
            $vigor = Find-Power -Creature $playerAfter -Pattern "VIGOR_POWER"

            Assert-Equal -Actual ([int]$playerAfter.block - [int]$playerBefore.block) -Expected 11 -Message "Patter+ block gained was wrong."
            Assert-True -Condition ($null -ne $vigor) -Message "Patter+ did not apply Vigor."
            Assert-Equal -Actual ([int]$vigor.amount) -Expected 3 -Message "Patter+ Vigor amount was wrong."

            return "Granted 11 block and 3 Vigor."
        }
    },
    @{
        Name = "FallingStar"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "FALLING_STAR"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Damage") -Expected 8 -Message "FallingStar damage was wrong."

            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0
            $starsBefore = [int]@($beforeState.run.players | Where-Object { $_.isLocalPlayer })[0].stars
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 0
            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            $starsAfter = [int]@($afterState.run.players | Where-Object { $_.isLocalPlayer })[0].stars
            $weak = Find-Power -Creature $enemyAfter -Pattern "WEAK_POWER"
            $vulnerable = Find-Power -Creature $enemyAfter -Pattern "VULNERABLE_POWER"

            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 8 -Message "FallingStar damage dealt was wrong."
            Assert-Equal -Actual ($starsAfter - $starsBefore) -Expected -2 -Message "FallingStar star cost was wrong."
            Assert-True -Condition ($null -ne $weak) -Message "FallingStar did not apply Weak."
            Assert-True -Condition ($null -ne $vulnerable) -Message "FallingStar did not apply Vulnerable."
            Assert-Equal -Actual ([int]$weak.amount) -Expected 1 -Message "FallingStar Weak amount was wrong."
            Assert-Equal -Actual ([int]$vulnerable.amount) -Expected 1 -Message "FallingStar Vulnerable amount was wrong."

            return "Dealt 8 damage and applied 1 Weak plus 1 Vulnerable."
        }
    },
    @{
        Name = "FallingStar+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "FALLING_STAR" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Damage") -Expected 12 -Message "FallingStar+ damage was wrong."

            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0
            $starsBefore = [int]@($beforeState.run.players | Where-Object { $_.isLocalPlayer })[0].stars
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 0
            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            $starsAfter = [int]@($afterState.run.players | Where-Object { $_.isLocalPlayer })[0].stars
            $weak = Find-Power -Creature $enemyAfter -Pattern "WEAK_POWER"
            $vulnerable = Find-Power -Creature $enemyAfter -Pattern "VULNERABLE_POWER"

            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 12 -Message "FallingStar+ damage dealt was wrong."
            Assert-Equal -Actual ($starsAfter - $starsBefore) -Expected -2 -Message "FallingStar+ star cost was wrong."
            Assert-Equal -Actual ([int]$weak.amount) -Expected 1 -Message "FallingStar+ Weak amount was wrong."
            Assert-Equal -Actual ([int]$vulnerable.amount) -Expected 1 -Message "FallingStar+ Vulnerable amount was wrong."

            return "Dealt 12 damage and still only applied 1 Weak plus 1 Vulnerable."
        }
    },
    @{
        Name = "WroughtInWar"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "WROUGHT_IN_WAR"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Forge") -Expected 7 -Message "WroughtInWar forge was wrong."

            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 0
            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 7 -Message "WroughtInWar damage dealt was wrong."

            $hand = Get-Hand
            $blade = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "SOVEREIGN_BLADE"
            Assert-True -Condition ($null -ne $blade) -Message "WroughtInWar did not create SovereignBlade."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $blade -Key "Damage") -Expected 17 -Message "WroughtInWar forged SovereignBlade damage was wrong."

            return "Dealt 7 damage and forged a 17-damage SovereignBlade."
        }
    },
    @{
        Name = "WroughtInWar+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "WROUGHT_IN_WAR" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Forge") -Expected 9 -Message "WroughtInWar+ forge was wrong."

            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 0
            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 9 -Message "WroughtInWar+ damage dealt was wrong."

            $hand = Get-Hand
            $blade = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "SOVEREIGN_BLADE"
            Assert-Equal -Actual (Get-DynamicIntValue -Card $blade -Key "Damage") -Expected 19 -Message "WroughtInWar+ forged SovereignBlade damage was wrong."

            return "Dealt 9 damage and forged a 19-damage SovereignBlade."
        }
    },
    @{
        Name = "Parry"
        Body = {
            Start-IsolatedCombat
            $parrySpawn = Spawn-Card -CardId "PARRY"
            $parry = @($parrySpawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $parry -Key "ParryPower") -Expected 10 -Message "Parry block value was wrong."

            $null = Play-Card -HandIndex ([int]$parry.handIndex) -TargetSelf
            $afterParry = Get-State
            $playerAfterParry = Get-LocalPlayerCreature -State $afterParry
            $parryPower = Find-Power -Creature $playerAfterParry -Pattern "PARRY_POWER"
            Assert-True -Condition ($null -ne $parryPower) -Message "Parry power was not applied."
            Assert-Equal -Actual ([int]$parryPower.amount) -Expected 10 -Message "Parry power amount was wrong."

            $bladeSpawn = Spawn-Card -CardId "SOVEREIGN_BLADE"
            $blade = @($bladeSpawn.cards)[0]
            $beforeBladeState = Get-State
            $playerBeforeBlade = Get-LocalPlayerCreature -State $beforeBladeState
            $null = Play-Card -HandIndex ([int]$blade.handIndex) -EnemyIndex 0
            $afterBladeState = Get-State
            $playerAfterBlade = Get-LocalPlayerCreature -State $afterBladeState
            Assert-Equal -Actual ([int]$playerAfterBlade.block - [int]$playerBeforeBlade.block) -Expected 10 -Message "Parry did not grant the correct block after SovereignBlade."

            return "ParryPower=10, and SovereignBlade granted 10 block after being played."
        }
    },
    @{
        Name = "Parry+"
        Body = {
            Start-IsolatedCombat
            $parrySpawn = Spawn-Card -CardId "PARRY" -UpgradeCount 1
            $parry = @($parrySpawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $parry -Key "ParryPower") -Expected 14 -Message "Parry+ block value was wrong."

            $null = Play-Card -HandIndex ([int]$parry.handIndex) -TargetSelf
            $afterParry = Get-State
            $playerAfterParry = Get-LocalPlayerCreature -State $afterParry
            $parryPower = Find-Power -Creature $playerAfterParry -Pattern "PARRY_POWER"
            Assert-Equal -Actual ([int]$parryPower.amount) -Expected 14 -Message "Parry+ power amount was wrong."

            $bladeSpawn = Spawn-Card -CardId "SOVEREIGN_BLADE"
            $blade = @($bladeSpawn.cards)[0]
            $beforeBladeState = Get-State
            $playerBeforeBlade = Get-LocalPlayerCreature -State $beforeBladeState
            $null = Play-Card -HandIndex ([int]$blade.handIndex) -EnemyIndex 0
            $afterBladeState = Get-State
            $playerAfterBlade = Get-LocalPlayerCreature -State $afterBladeState
            Assert-Equal -Actual ([int]$playerAfterBlade.block - [int]$playerBeforeBlade.block) -Expected 14 -Message "Parry+ did not grant the correct block after SovereignBlade."

            return "ParryPower=14, and SovereignBlade granted 14 block after being played."
        }
    },
    @{
        Name = "Glitterstream"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "GLITTERSTREAM"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "BlockNextTurn") -Expected 5 -Message "Glitterstream BlockNextTurn was wrong."

            $beforeState = Get-State
            $playerBefore = Get-LocalPlayerCreature -State $beforeState
            $null = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            $afterState = Get-State
            $playerAfter = Get-LocalPlayerCreature -State $afterState
            $blockNextTurn = Find-Power -Creature $playerAfter -Pattern "BLOCK_NEXT_TURN_POWER"

            Assert-Equal -Actual ([int]$playerAfter.block - [int]$playerBefore.block) -Expected 11 -Message "Glitterstream immediate block was wrong."
            Assert-True -Condition ($null -ne $blockNextTurn) -Message "Glitterstream did not apply BlockNextTurnPower."
            Assert-Equal -Actual ([int]$blockNextTurn.amount) -Expected 5 -Message "Glitterstream next-turn block amount was wrong."

            return "Granted 11 immediate block and 5 BlockNextTurn."
        }
    },
    @{
        Name = "Glitterstream+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "GLITTERSTREAM" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "BlockNextTurn") -Expected 7 -Message "Glitterstream+ BlockNextTurn was wrong."

            $beforeState = Get-State
            $playerBefore = Get-LocalPlayerCreature -State $beforeState
            $null = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            $afterState = Get-State
            $playerAfter = Get-LocalPlayerCreature -State $afterState
            $blockNextTurn = Find-Power -Creature $playerAfter -Pattern "BLOCK_NEXT_TURN_POWER"

            Assert-Equal -Actual ([int]$playerAfter.block - [int]$playerBefore.block) -Expected 13 -Message "Glitterstream+ immediate block was wrong."
            Assert-Equal -Actual ([int]$blockNextTurn.amount) -Expected 7 -Message "Glitterstream+ next-turn block amount was wrong."

            return "Granted 13 immediate block and 7 BlockNextTurn."
        }
    },
    @{
        Name = "CelestialMight"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "CELESTIAL_MIGHT"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Damage") -Expected 6 -Message "CelestialMight damage was wrong."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Repeat") -Expected 3 -Message "CelestialMight repeat count was wrong."

            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 1
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 1
            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 1
            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 18 -Message "CelestialMight dealt the wrong total damage."

            return "Dealt 6x3 = 18 total damage."
        }
    },
    @{
        Name = "CelestialMight+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "CELESTIAL_MIGHT" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Damage") -Expected 6 -Message "CelestialMight+ damage should stay 6."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Repeat") -Expected 4 -Message "CelestialMight+ repeat count was wrong."

            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 1
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 1
            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 1
            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 24 -Message "CelestialMight+ dealt the wrong total damage."

            return "Upgrade kept damage at 6 and raised hit count to 4 for 24 total damage."
        }
    },
    @{
        Name = "RefineBlade"
        Body = {
            Start-IsolatedCombat -BonusEnergy 0 -BonusStars 0
            $spawn = Spawn-Card -CardId "REFINE_BLADE"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Forge") -Expected 9 -Message "RefineBlade forge was wrong."

            $null = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            $afterPlayState = Get-State
            $playerAfterPlay = Get-LocalPlayerCreature -State $afterPlayState
            $energyNextTurn = Find-Power -Creature $playerAfterPlay -Pattern "ENERGY_NEXT_TURN_POWER"
            Assert-True -Condition ($null -ne $energyNextTurn) -Message "RefineBlade did not apply EnergyNextTurnPower."
            Assert-Equal -Actual ([int]$energyNextTurn.amount) -Expected 1 -Message "RefineBlade next-turn energy amount was wrong."

            $hand = Get-Hand
            $blade = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "SOVEREIGN_BLADE"
            Assert-True -Condition ($null -ne $blade) -Message "RefineBlade did not create SovereignBlade."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $blade -Key "Damage") -Expected 19 -Message "RefineBlade forged SovereignBlade damage was wrong."

            $null = End-Turn
            Wait-For-PlayerTurn
            Wait-Until -TimeoutMs 5000 -PollMs 100 -Message "RefineBlade next-turn energy bonus did not settle." -Condition {
                $settledState = Get-State
                return [int]@($settledState.run.players | Where-Object { $_.isLocalPlayer })[0].energy -eq 4
            }
            $nextState = Get-State
            $nextEnergy = [int]@($nextState.run.players | Where-Object { $_.isLocalPlayer })[0].energy
            Assert-Equal -Actual $nextEnergy -Expected 4 -Message "RefineBlade did not grant +1 energy next turn."

            return "Forged for 9, applied EnergyNextTurn=1, and next turn energy became 4."
        }
    },
    @{
        Name = "RefineBlade+"
        Body = {
            Start-IsolatedCombat -BonusEnergy 0 -BonusStars 0
            $spawn = Spawn-Card -CardId "REFINE_BLADE" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Forge") -Expected 13 -Message "RefineBlade+ forge was wrong."

            $null = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            $afterPlayState = Get-State
            $playerAfterPlay = Get-LocalPlayerCreature -State $afterPlayState
            $energyNextTurn = Find-Power -Creature $playerAfterPlay -Pattern "ENERGY_NEXT_TURN_POWER"
            Assert-Equal -Actual ([int]$energyNextTurn.amount) -Expected 1 -Message "RefineBlade+ next-turn energy amount was wrong."

            $hand = Get-Hand
            $blade = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "SOVEREIGN_BLADE"
            Assert-Equal -Actual (Get-DynamicIntValue -Card $blade -Key "Damage") -Expected 23 -Message "RefineBlade+ forged SovereignBlade damage was wrong."

            $null = End-Turn
            Wait-For-PlayerTurn
            Wait-Until -TimeoutMs 5000 -PollMs 100 -Message "RefineBlade+ next-turn energy bonus did not settle." -Condition {
                $settledState = Get-State
                return [int]@($settledState.run.players | Where-Object { $_.isLocalPlayer })[0].energy -eq 4
            }
            $nextState = Get-State
            $nextEnergy = [int]@($nextState.run.players | Where-Object { $_.isLocalPlayer })[0].energy
            Assert-Equal -Actual $nextEnergy -Expected 4 -Message "RefineBlade+ did not grant +1 energy next turn."

            return "Forged for 13, applied EnergyNextTurn=1, and next turn energy became 4."
        }
    },
    @{
        Name = "SwordSage"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "SWORD_SAGE"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual ([int]$card.energyCostCurrent) -Expected 2 -Message "SwordSage cost was wrong."

            $null = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            $null = Spawn-Card -CardId "SOVEREIGN_BLADE"
            $hand = Get-Hand
            $blade = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "SOVEREIGN_BLADE"
            Assert-True -Condition ($null -ne $blade) -Message "SwordSage test could not find SovereignBlade."
            Assert-Equal -Actual ([int]$blade.energyCostCurrent) -Expected 2 -Message "SwordSage should no longer increase SovereignBlade cost."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $blade -Key "Repeat") -Expected 2 -Message "SwordSage should still increase SovereignBlade repeat count."

            return "Left SovereignBlade at cost 2 and raised its repeat count to 2."
        }
    },
    @{
        Name = "SwordSage+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "SWORD_SAGE" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual ([int]$card.energyCostCurrent) -Expected 1 -Message "SwordSage+ cost was wrong."

            $null = Play-Card -HandIndex ([int]$card.handIndex) -TargetSelf
            $null = Spawn-Card -CardId "SOVEREIGN_BLADE"
            $hand = Get-Hand
            $blade = Get-FirstMatchingCard -Cards @($hand.cards) -CardId "SOVEREIGN_BLADE"
            Assert-Equal -Actual ([int]$blade.energyCostCurrent) -Expected 2 -Message "SwordSage+ should no longer increase SovereignBlade cost."
            Assert-Equal -Actual (Get-DynamicIntValue -Card $blade -Key "Repeat") -Expected 2 -Message "SwordSage+ should still increase SovereignBlade repeat count."

            return "Cost dropped to 1, while SovereignBlade stayed at cost 2 and repeat 2."
        }
    },
    @{
        Name = "GuidingStar"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "GUIDING_STAR"
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Cards") -Expected 2 -Message "GuidingStar draw count was wrong."

            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0
            $beforeHand = Get-Hand
            $beforeHandCount = Get-PileCardCount -PileSnapshot $beforeHand
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 0
            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            $afterHand = Get-Hand
            $playerAfter = Get-LocalPlayerCreature -State $afterState
            $drawNextTurn = Find-Power -Creature $playerAfter -Pattern "DRAW_CARDS_NEXT_TURN_POWER"

            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 12 -Message "GuidingStar damage dealt was wrong."
            Assert-Equal -Actual ((Get-PileCardCount -PileSnapshot $afterHand) - $beforeHandCount) -Expected 1 -Message "GuidingStar did not draw immediately this turn."
            Assert-True -Condition ($null -eq $drawNextTurn) -Message "GuidingStar should not apply DrawCardsNextTurnPower anymore."

            return "Dealt 12 damage, drew 2 immediately, and left no DrawCardsNextTurnPower."
        }
    },
    @{
        Name = "GuidingStar+"
        Body = {
            Start-IsolatedCombat
            $spawn = Spawn-Card -CardId "GUIDING_STAR" -UpgradeCount 1
            $card = @($spawn.cards)[0]
            Assert-Equal -Actual (Get-DynamicIntValue -Card $card -Key "Cards") -Expected 3 -Message "GuidingStar+ draw count was wrong."

            $beforeState = Get-State
            $enemyBefore = Get-EnemyCreature -State $beforeState -Index 0
            $beforeHand = Get-Hand
            $beforeHandCount = Get-PileCardCount -PileSnapshot $beforeHand
            $null = Play-Card -HandIndex ([int]$card.handIndex) -EnemyIndex 0
            $afterState = Get-State
            $enemyAfter = Get-EnemyCreature -State $afterState -Index 0
            $afterHand = Get-Hand
            $playerAfter = Get-LocalPlayerCreature -State $afterState
            $drawNextTurn = Find-Power -Creature $playerAfter -Pattern "DRAW_CARDS_NEXT_TURN_POWER"

            Assert-Equal -Actual ([int]$enemyBefore.currentHp - [int]$enemyAfter.currentHp) -Expected 13 -Message "GuidingStar+ damage dealt was wrong."
            Assert-Equal -Actual ((Get-PileCardCount -PileSnapshot $afterHand) - $beforeHandCount) -Expected 2 -Message "GuidingStar+ did not draw immediately this turn."
            Assert-True -Condition ($null -eq $drawNextTurn) -Message "GuidingStar+ should not apply DrawCardsNextTurnPower anymore."

            return "Dealt 13 damage, drew 3 immediately, and left no DrawCardsNextTurnPower."
        }
    }
)

Resolve-LogPath

if ($Only -and $Only.Count -gt 0) {
    $tests = @($tests | Where-Object { $Only -contains $_.Name })
}

foreach ($test in $tests) {
    Invoke-TestCase -Name $test.Name -Body $test.Body
}

Save-Results

$passed = @($script:Results | Where-Object { $_.Passed }).Count
$failed = @($script:Results | Where-Object { -not $_.Passed }).Count

Write-Host ""
Write-LogLine ("Summary: {0} passed, {1} failed." -f $passed, $failed)
Write-LogLine ("Results saved to: {0}" -f $script:ResolvedOutputPath)
Write-LogLine ("Log saved to: {0}" -f $script:ResolvedLogPath)

if ($failed -gt 0) {
    exit 1
}
