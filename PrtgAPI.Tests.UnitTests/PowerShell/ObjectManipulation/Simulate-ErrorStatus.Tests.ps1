﻿. $PSScriptRoot\..\..\Support\PowerShell\Standalone.ps1

Describe "Simulate-ErrorStatus" -Tag @("PowerShell", "UnitTest") {
    SetActionResponse

    It "can execute" {
        $sensor = Run Sensor { Get-Sensor }

        $sensor | Simulate-ErrorStatus
    }

    It "can execute with -WhatIf" {
        $sensor = Run Sensor { Get-Sensor }

        $sensor | Simulate-ErrorStatus -WhatIf
    }

    It "executes with -Batch:`$true" {

        SetMultiTypeResponse

        $sensors = Get-Sensor -Count 2

        SetAddressValidatorResponse @(
            "api/simulate.htm?id=4000,4001&action=1&"
        )

        $sensors | Simulate-ErrorStatus -Batch:$true
    }

    It "executes with -Batch:`$false" {

        SetMultiTypeResponse

        $sensors = Get-Sensor -Count 2

        SetAddressValidatorResponse @(
            "api/simulate.htm?id=4000&action=1&"
            "api/simulate.htm?id=4001&action=1&"
        )

        $sensors | Simulate-ErrorStatus -Batch:$false
    }

    It "passes through with -Batch:`$false" {
        SetMultiTypeResponse

        $sensor = Get-Sensor -Count 1

        $newSensor = $sensor | Simulate-ErrorStatus -PassThru -Batch:$false

        $newSensor | Should Be $sensor
    }

    It "passes through with -Batch:`$true" {
        SetMultiTypeResponse

        $sensor = Get-Sensor -Count 1

        $newSensor = $sensor | Simulate-ErrorStatus -PassThru -Batch:$true

        $newSensor | Should Be $sensor
    }
}