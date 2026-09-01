# Test scope

Mirror production ownership under `BoscaliSummer.Tests/Features/<Feature>`, `Framework`,
and `Architecture`. When editing one feature, open and change only its matching test folder
plus the small test runner if registration is necessary.

`Architecture` tests may inspect source layout and dependency direction but must not encode
gameplay behavior. `BoscaliSummer.PatchProbe` is a compatibility gate; change it only for a
Harmony target, reflected game member, module/patch inventory, or wire-contract change.
