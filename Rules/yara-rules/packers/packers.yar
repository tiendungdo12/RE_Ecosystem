/*
    Packer and Protector Signatures for RE Ecosystem Deterministic Triage
*/

rule UPX {
    meta:
        description = "UPX Executable Packer"
        author = "RE Ecosystem"
    strings:
        $upx0 = "UPX0" ascii
        $upx1 = "UPX1" ascii
        $upx2 = "UPX2" ascii
        $upx_sig = "UPX!" ascii
    condition:
        2 of ($upx*)
}

rule ASPack {
    meta:
        description = "ASPack Executable Protector"
        author = "RE Ecosystem"
    strings:
        $aspack1 = ".aspack" ascii nocase
        $aspack2 = "ASPack" ascii
    condition:
        any of them
}

rule PECompact {
    meta:
        description = "PECompact Protector"
        author = "RE Ecosystem"
    strings:
        $pec1 = "PECompact2" ascii
        $pec2 = "PEC2" ascii
    condition:
        any of them
}

rule Themida {
    meta:
        description = "Themida / WinLicense Protector"
        author = "RE Ecosystem"
    strings:
        $themida1 = "Themida" ascii nocase
        $themida2 = ".themida" ascii nocase
    condition:
        any of them
}

rule VMProtect {
    meta:
        description = "VMProtect Software Protection"
        author = "RE Ecosystem"
    strings:
        $vmp1 = ".vmp0" ascii nocase
        $vmp2 = ".vmp1" ascii nocase
        $vmp3 = "VMProtect" ascii
    condition:
        any of them
}
