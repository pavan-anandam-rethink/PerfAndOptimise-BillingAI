# Writes 1,000 synthetic X12 5010 files (250 each of 999, 277CA, 835, TA1) for the dev SFTP folder.
# Every name, id, and amount is made up. There is no PHI. Re-running replaces samples/inbound/dev-edi.
$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'dev-edi'
if (Test-Path $output) { Remove-Item -Recurse -Force $output }
New-Item -ItemType Directory -Path $output | Out-Null

function New-Isa([int]$Control) {
    $id = $Control.ToString('000000000')
    $isa = "ISA*00*          *00*          *ZZ*AVAILITY       *ZZ*AV09311993     *261107*0900*^*00501*$id*0*T*:"
    # Same envelope width as the four checked-in samples (the service stores the bytes; it does not parse X12).
    if ($isa.Length -ne 105) { throw "ISA length is $($isa.Length) for control $Control." }
    return $isa
}

$counts = @{ '999' = 0; '277CA' = 0; '835' = 0; 'TA1' = 0 }
for ($n = 1; $n -le 1000; $n++) {
    $isa = New-Isa $n
    $gs = $n.ToString()
    $claim = 'CLAIM{0:D4}' -f $n
    switch ($n % 4) {
        1 {
            $kind = '999'
            $body = @"
${isa}~GS*FA*AVAILITY*AV09311993*20261107*0900*$gs*X*005010X231A1~ST*999*0001*005010X231A1~AK1*HC*$gs*005010X222A1~AK2*837*0001*005010X222A1~IK5*A~AK9*A*1*1*1~SE*6*0001~GE*1*$gs~IEA*1*$($n.ToString('000000000'))~
"@
        }
        2 {
            $kind = '277CA'
            $body = @"
${isa}~GS*HN*AVAILITY*AV09311993*20261107*0900*$gs*X*005010X214~ST*277*0001*005010X214~BHT*0085*08*$claim*20261107*0900*TH~HL*1**20*1~NM1*PR*2*TEST PAYER*****PI*99999~TRN*1*TRACE$($n.ToString('0000'))~DTP*050*D8*20261107~DTP*009*D8*20261107~HL*2*1*21*1~NM1*41*2*TEST BILLER*****46*030240928~TRN*2*BATCH$($n.ToString('0000'))~STC*A1:20*20261107*WQ*100.00~QTY*90*1~AMT*YU*100.00~HL*3*2*19*1~NM1*85*2*TEST PROVIDER*****XX*1234567893~HL*4*3*PT~NM1*QC*1*DOE*TEST****MI*TEST$($n.ToString('000000'))~TRN*2*$claim~STC*A2:20*20261107*WQ*100.00~REF*1K*PAYER$claim~DTP*472*RD8*20261001-20261001~SE*22*0001~GE*1*$gs~IEA*1*$($n.ToString('000000000'))~
"@
        }
        3 {
            $kind = '835'
            $body = @"
${isa}~GS*HP*AVAILITY*AV09311993*20261107*0900*$gs*X*005010X221A1~ST*835*0001~BPR*I*80.00*C*ACH*CCP*01*999999999*DA*000000001*1999999999**01*888888888*DA*000000002*20261107~TRN*1*EFT$($n.ToString('0000'))*1999999999~DTM*405*20261107~N1*PR*TEST PAYER~N3*1 TEST WAY~N4*TESTVILLE*TX*75001~REF*2U*99999~N1*PE*TEST PROVIDER*XX*1234567893~CLP*$claim*1*100.00*80.00*20.00*12*PAYER$claim*11*1~NM1*QC*1*DOE*TEST****MI*TEST$($n.ToString('000000'))~SVC*HC:97153*100.00*80.00**4~DTM*472*20261001~CAS*PR*2*20.00~SE*15*0001~GE*1*$gs~IEA*1*$($n.ToString('000000000'))~
"@
        }
        0 {
            $kind = 'TA1'
            $body = @"
${isa}~TA1*$($n.ToString('000000000'))*261107*0900*A*000~IEA*0*$($n.ToString('000000000'))~
"@
        }
    }

    $name = 'AV_{0}_20261107_{1:D4}.x12' -f $kind, $n
    [System.IO.File]::WriteAllText((Join-Path $output $name), $body.Trim())
    $counts[$kind]++
}

$written = (Get-ChildItem $output -Filter '*.x12').Count
if ($written -ne 1000) { throw "Expected 1000 files, wrote $written." }
$counts.GetEnumerator() | ForEach-Object { if ($_.Value -ne 250) { throw "$($_.Key) count is $($_.Value)." } }
Write-Output "Wrote $written files to $output"
