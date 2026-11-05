# BDIS Selenium Refactoring Notes

## 1. Scope and sources

This refactoring replaces the legacy BDIS test methods with the functional flow recorded in `BDIS.side` while preserving the existing LINQPad infrastructure.

| Source | Role |
| --- | --- |
| `BDIS/BDIS.side` | Functional source of truth: order, navigation, selectors, values, validations, country-specific behavior. |
| `BDIS/BDISWEB-SANDY/BDIS-SELENIUM.Linq` | New implementation. |
| `BDIS/BDISWEB-SANDY/BDIS-SELENIUM.Linq.bak` | Original script for rollback and infrastructure reference. |
| `BDIS/BDISWEB-SANDY/Config.properties` | New configuration. |
| `BDIS/BDISWEB-SANDY/Config.properties.bak` | Original configuration for rollback. |
| `BDIS/INSTRUCCIONES.txt` | Refactoring requirements. |

Changed files:

- `BDIS/BDISWEB-SANDY/BDIS-SELENIUM.Linq`
- `BDIS/BDISWEB-SANDY/Config.properties`
- `BDIS/REFACTORING-NOTES.md` (this document)

No changes were made to the Power Automate flow, Parse JSON schema, `FilasTarjetas`, or Adaptive Card.

## 2. Updated `Config.properties` template

The deployed file keeps the same key/value format and comments. Secret values are intentionally shown as placeholders here; the real values are entered directly in `Config.properties` as required.

```properties
# Target environments
f5Url=https://www.brok.dis.ups.com/login.aspx
webServerUrl2=https://su6bdisweb2
webServerUrl3=https://su6bdisweb3
webServerUrl4=https://su6bdisweb4
webServerUrl5=https://su6bdisweb5

# Universal credentials used for every configured environment
username=<username>
password=<password>

# Country-specific shipment parameters
caShipmentNbr=<canada-document-query-shipment>
caShipmentNbrPK=<canada-packet-print-shipment>
usShipmentNbr=<us-document-query-shipment>
usShipmentNbrPK=<us-packet-print-shipment>

# Recipient used by the Document Query email step
emailRecipient=<recipient>

# Environments where a Document Query email failure is a non-fatal warning
emailWarningEnvironments=F5,SU6BDISWEB2,SU6BDISWEB3,SU6BDISWEB4
emailWarningTimeoutSeconds=20

# Power Automate webhook endpoint
powerAutomateWebhookUrl=<webhook-url>

# Selenium timeouts in seconds
# Defaults used when a value is absent: 30, 60 and 120 respectively.
elementTimeoutSeconds=30
pageLoadTimeoutSeconds=60
uploadTimeoutSeconds=120
```

### Old-to-new key mapping

| Old key | New key | Reason |
| --- | --- | --- |
| `url1` | `f5Url` | Clearer name for the F5 target. |
| `url2` | `webServerUrl2` | Clearer name for web server 2. |
| `url3` | `webServerUrl3` | Clearer name for web server 3. |
| `url4` | `webServerUrl4` | Clearer name for web server 4. |
| `url5` | `webServerUrl5` | Clearer name for web server 5. |
| `usrName1..usrName5` | `username` | One universal credential pair. |
| `usrPass1..usrPass5` | `password` | One universal credential pair. |
| `caShipmentNbr` | `caShipmentNbr` | Retained for Canada Document Query; value updated from `BDIS.side`. |
| `usShipmentNbr` | `usShipmentNbr` | Retained for US Document Query; value updated from `BDIS.side`. |
| n/a | `caShipmentNbrPK` | Canada Packet Print shipment from `BDIS.side`. |
| n/a | `usShipmentNbrPK` | US Packet Print shipment from `BDIS.side`. |
| n/a | `emailRecipient` | Recipient used by the email step from `BDIS.side`. |
| n/a | `emailWarningEnvironments` | Environments where an email failure is a non-fatal warning. |
| n/a | `emailWarningTimeoutSeconds` | Shorter email wait used only on warning environments. |
| n/a | `elementTimeoutSeconds` | Explicit element wait timeout. |
| n/a | `pageLoadTimeoutSeconds` | Explicit page load timeout. |
| n/a | `uploadTimeoutSeconds` | Explicit image upload timeout. |

Removed obsolete keys:

`url1..url5`, `usrName1..usrPass5`, `usrNameTxtBox`, `usrPassTxtBox`, `loginBtn`, `countryLogin`, `countryCode`, `shipStartDate`, `shipEndDate`, `dutyType`, `mngAcctUser`, `brkrCode`, `newBrokName`.

Verification:

- All removed keys were searched across the rewritten script and configuration.
- No active reference to the old keys remains.
- `url1..url5` are no longer read anywhere; URLs are resolved centrally through `ConfigurationManager.CreateUrlProfiles`.

## 3. Class structure

| Class / type | Responsibility |
| --- | --- |
| `ProgramSettings` | Deployed paths, test name, log folder. |
| `TestExecutionMode` | `F5Only` and `FullTest`. |
| `UrlTestProfile` | One target URL and its `webKey`. |
| `CountryTestProfile` | Country code, country label, shipment values, packet label. |
| `TestStepResult` | One logical step result. |
| `CountryTestResult` | One country result inside one environment. |
| `EnvironmentTestResult` | Aggregated result for one URL; webhook row. |
| `RunSummary` | Global run status and all results. |
| `ConfigurationException` | Invalid or incomplete configuration. |
| `EdgeDriverInitializationException` | Edge WebDriver startup failure. |
| `SecretRedactor` | Removes credentials, webhook URL and email from logs, reports, dialogs and errors. |
| `ConfigurationManager` | Loads, validates and exposes configuration; builds URL and country profiles. |
| `ExecutionLogger` | Tab-separated local log with redaction. |
| `HtmlReportGenerator` | Existing dashboard-style HTML report. |
| `PowerAutomateReporter` | Builds and posts the existing object envelope. |
| `JsonHelper` | JSON escaping for the manually built payload. |
| `ScreenshotHelper` | Failure screenshots. |
| `SeleniumWaitHelper` | Explicit waits, alerts, page text and new-window handling. |
| `CountrySelectionService` | Selects and validates the country. |
| `BdisLoginService` | Login and logout for each target URL. |
| `BdisWorkflow` | Document Query, Packet Print and Manipulate Image workflow. |
| `EdgeDriverManager` | Driver preflight version check and Edge creation. |
| `DialogService` | Mode selection, non-expiring EdgeDriver help, error dialogs. |
| `BdisTestRunner` | Orchestrates environments, countries, steps and aggregation. |
| `Main` | Entry point; prerequisite failures; report and webhook dispatch. |

Removed legacy test methods:

`Login`, `DocumentQuery`, `WebStatus`, `PacketPrint`, `DocType`, `UploadDocument`, `AccountManagement`, `ManipulateImage`.

These are replaced by `BdisLoginService`, `BdisWorkflow`, and `BdisTestRunner`. The infrastructure they used (evaluation, timing, error capture, screenshots, log, HTML report, environment status, global status, Power Automate payload) is preserved.

## 4. `BDIS.side` command mapping

### 4.1 CANADA test

| # | SIDE command | Target / value | C# implementation |
| --- | --- | --- | --- |
| 0 | `open` | `https://www.brok.dis.ups.com/login.aspx` | `BdisLoginService.Login` -> `driver.Navigate().GoToUrl(url)` using `f5Url`. |
| 1 | `setWindowSize` | `1957x1380` | `BdisTestRunner.RunEnvironment` -> `Window.Maximize()`. Exact size is not required by the workflow; maximize avoids resolution-specific failures. |
| 2 | `click` | `css=.crModuleLabelLine:nth-child(4) > td` | No-op focus click; focus is implicit in the following field interactions. |
| 3-6 | `click` / `doubleClick` | `id=MainContent_ebPassword` | `BdisLoginService.Login` -> `Click`, `Clear`, `SendKeys(password)`. |
| 7 | `click` | `id=MainContent_ddlCountry` | `CountrySelectionService.SelectCountry` selects `Canada` deterministically even though the SIDE recording omits an explicit `select`. |
| 8 | `click` | `id=MainContent_btnLogin` | `BdisLoginService.Login` -> `WaitClickable(...).Click()`. |
| 9 | `click` | `id=tbVal1_1` | `BdisWorkflow.DocumentQuerySearch` -> `Click`, `Clear`, `SendKeys(caShipmentNbr)`. |
| 10 | `type` | `id=tbVal1_1` = `121429J4K3H` | `BdisWorkflow.DocumentQuerySearch` using `caShipmentNbr`. |
| 11 | `click` | `css=.bdisForm` | No-op focus click. |
| 12 | `click` | `id=btnSearch` | `BdisWorkflow.DocumentQuerySearch`. |
| 13 | `click` | `id=MainContent_Checkbox1` | `BdisWorkflow.DocumentQueryPrint`. |
| 14 | `click` | `id=MainContent_btnPrint` | `BdisWorkflow.DocumentQueryPrint`. |
| 15 | `storeWindowHandle` | `root` | `BdisWorkflow.DocumentQueryPrint` records `driver.CurrentWindowHandle`. |
| 16 | `selectWindow` | recorded print window | `BdisWorkflow.CloseNewWindowAndReturn` switches to the new handle. |
| 17 | `close` | - | `BdisWorkflow.CloseNewWindowAndReturn` -> `driver.Close()`. |
| 18 | `selectWindow` | `root` | `BdisWorkflow.CloseNewWindowAndReturn` switches back to the main handle. |
| 19 | `click` | `id=MainContent_btnEmail` | `BdisWorkflow.DocumentQueryEmail`. |
| 20 | `click` | `id=MainContent_txtEmailTo` | `BdisWorkflow.DocumentQueryEmail` -> `Click`, `Clear`, `SendKeys(emailRecipient)`. |
| 21 | `type` | `id=MainContent_txtEmailTo` = recipient | `BdisWorkflow.DocumentQueryEmail` using `emailRecipient`. |
| 22 | `click` | `id=MainContent_txtEmailBody` | `BdisWorkflow.DocumentQueryEmail`. |
| 23 | `type` | `id=MainContent_txtEmailBody` = `BDIS TEST` | `BdisWorkflow.DocumentQueryEmail`. |
| 24 | `click` | `id=MainContent_BtnSend` | `BdisWorkflow.DocumentQueryEmail`. |
| 25 | `click` | `css=#body td:nth-child(2)` | No-op focus click after send. |
| 26-27 | `click` | `css=.bcopyright` | No-op focus clicks. |
| 28 | `click` | `id=hlDocumentQuery` | `BdisWorkflow.DocumentQueryEmail` returns to Document Query for deterministic Packet Print navigation. |
| 29 | `click` | `id=hlPacketPrint` | `BdisWorkflow.PacketPrintSelectAndSearch`. |
| 30 | `click` | `id=MainContent_PacketName` | `BdisWorkflow.PacketPrintSelectAndSearch` -> `SelectElement`. |
| 31 | `select` | `id=MainContent_PacketName` = `label=Commercial Invoice` | `BdisWorkflow.SelectPacketByName` using `CountryTestProfile.PacketNameLabel`. |
| 32 | `click` | `id=MainContent_Box1` | `BdisWorkflow.PacketPrintSelectAndSearch` -> `Click`, `Clear`, `SendKeys(caShipmentNbrPK)`. |
| 33 | `type` | `id=MainContent_Box1` = `782717VVJ4B` | `BdisWorkflow.PacketPrintSelectAndSearch` using `caShipmentNbrPK`. |
| 34 | `click` | `id=MainContent_GetPacketsBtn` | `BdisWorkflow.PacketPrintSelectAndSearch`. |
| 35 | `click` | `id=MainContent_Grid1_Remove_0` | `BdisWorkflow.PacketPrintPrint`. |
| 36 | `click` | `id=MainContent_BtnPrint` | `BdisWorkflow.PacketPrintPrint`. |
| 37 | `selectWindow` | recorded print window | `BdisWorkflow.CloseNewWindowAndReturn`. |
| 38 | `close` | - | `BdisWorkflow.CloseNewWindowAndReturn`. |
| 39 | `selectWindow` | `root` | `BdisWorkflow.CloseNewWindowAndReturn`. |
| 40 | `click` | `id=hlManipulateImage` | `BdisWorkflow.UploadImage`. |
| 41 | `click` | `css=.formbuttonblue` | `BdisWorkflow.UploadImage` fallback only; the primary path sends the file directly to `input[type='file']`. |
| 42 | `type` | `css=body > input` = `C:\fakepath\Image.tif` | `BdisWorkflow.UploadImage` sends `ImageFilePath` (`BDISImg\Image.tif`). |
| 43 | `click` | `css=.iv-viewer > img` | `BdisWorkflow.EditImage`. |
| 44 | `click` | `css=.fa-rotate-left` | `BdisWorkflow.EditImage`. |
| 45 | `click` | `css=.fa-rotate-right` | `BdisWorkflow.EditImage`. |
| 46 | `click` | `css=.fa-search-plus` | `BdisWorkflow.EditImage`. |
| 47 | `click` | `css=.fa-search-minus` | `BdisWorkflow.EditImage`. |
| 48 | `click` | `css=.checkbox-select > .ng-untouched` | `BdisWorkflow.DeleteImage` tries `.ng-untouched`, then falls back to `.checkbox-select input[type='checkbox']`. |
| 49 | `chooseOkOnNextConfirmation` | - | `BdisWorkflow.DeleteImage` waits for the alert. |
| 50 | `click` | `css=.fa-trash` | `BdisWorkflow.DeleteImage`. |
| 51 | `assertConfirmation` | `Are you sure you want to delete the 1 selected image(s)?` | `BdisWorkflow.DeleteImage` validates `alert.Text` before accepting. |
| 52 | `webdriverChooseOkOnVisibleConfirmation` | - | `BdisWorkflow.DeleteImage` -> `alert.Accept()`. |
| 53 | `click` | `linkText=Log Out` | `BdisLoginService.Logout` prefers `lnkbLogOut`, then link text `Log Out`. |
| 54-55 | `type` | `MainContent_ebUserID` / `MainContent_ebPassword` | Recorded preparation for the next login. `BdisLoginService.Login` performs these actions at the start of each country run; they are not replayed after logout. |

### 4.2 US test

| # | SIDE command | Target / value | C# implementation |
| --- | --- | --- | --- |
| 0 | `open` | `https://www.brok.dis.ups.com/login.aspx` | `BdisLoginService.Login` using `f5Url`. |
| 1 | `setWindowSize` | `1957x1380` | `BdisTestRunner.RunEnvironment` -> `Window.Maximize()`. |
| 2 | `click` | `css=html` | No-op focus click. |
| 3 | `click` | `id=MainContent_ebPassword` | `BdisLoginService.Login` -> `Click`, `Clear`, `SendKeys(password)`. |
| 4 | `click` | `css=.crModuleLabelLine:nth-child(6) > td` | No-op focus click. |
| 5 | `click` | `id=MainContent_ddlCountry` | `CountrySelectionService.SelectCountry`. |
| 6 | `select` | `id=MainContent_ddlCountry` = `label=United States` | `CountrySelectionService.SelectCountry` using `CountrySelectText`. |
| 7 | `click` | `id=MainContent_btnLogin` | `BdisLoginService.Login`. |
| 8 | `click` | `id=tbVal1_1` | `BdisWorkflow.DocumentQuerySearch` -> `Click`, `Clear`, `SendKeys(usShipmentNbr)`. |
| 9 | `type` | `id=tbVal1_1` = `00R701G3FLT` | `BdisWorkflow.DocumentQuerySearch` using `usShipmentNbr`. |
| 10 | `click` | `id=btnSearch` | `BdisWorkflow.DocumentQuerySearch`. |
| 11 | `click` | `id=MainContent_Checkbox1` | `BdisWorkflow.DocumentQueryPrint`. |
| 12 | `click` | `id=MainContent_btnPrint` | `BdisWorkflow.DocumentQueryPrint`. |
| 13 | `storeWindowHandle` | `root` | `BdisWorkflow.DocumentQueryPrint` records `driver.CurrentWindowHandle`. |
| 14 | `selectWindow` | recorded print window | `BdisWorkflow.CloseNewWindowAndReturn`. |
| 15 | `close` | - | `BdisWorkflow.CloseNewWindowAndReturn`. |
| 16 | `selectWindow` | `root` | `BdisWorkflow.CloseNewWindowAndReturn`. |
| 17 | `click` | `id=MainContent_btnEmail` | `BdisWorkflow.DocumentQueryEmail`. |
| 18 | `click` | `id=MainContent_txtEmailTo` | `BdisWorkflow.DocumentQueryEmail`. |
| 19 | `type` | `id=MainContent_txtEmailTo` = recipient | `BdisWorkflow.DocumentQueryEmail` using `emailRecipient`. |
| 20 | `click` | `id=MainContent_txtEmailBody` | `BdisWorkflow.DocumentQueryEmail`. |
| 21 | `type` | `id=MainContent_txtEmailBody` = `BDIS TEST` | `BdisWorkflow.DocumentQueryEmail`. |
| 22 | `click` | `css=#MainContent_emailTable tr:nth-child(7) > td` | No-op focus click. |
| 23 | `click` | `id=MainContent_BtnSend` | `BdisWorkflow.DocumentQueryEmail`. |
| 24 | `click` | `css=.content-wrapper` | No-op focus click. |
| 25-26 | `click` | `css=.bcopyright` | No-op focus clicks. |
| 27 | `click` | `css=body` | No-op focus click. |
| 28 | `click` | `id=hlPacketPrint` | `BdisWorkflow.PacketPrintSelectAndSearch`; the method also returns to Document Query first after the email step. |
| 29 | `click` | `id=MainContent_PacketName` | `BdisWorkflow.PacketPrintSelectAndSearch` -> `SelectElement`. |
| 30 | `select` | `id=MainContent_PacketName` = `label=CI - Commerical Invoice` | `BdisWorkflow.SelectPacketByName` using `CountryTestProfile.PacketNameLabel` (application spelling preserved). |
| 31 | `click` | `id=MainContent_Box1` | `BdisWorkflow.PacketPrintSelectAndSearch` -> `Click`, `Clear`, `SendKeys(usShipmentNbrPK)`. |
| 32 | `type` | `id=MainContent_Box1` = `A20743KT8MJ` | `BdisWorkflow.PacketPrintSelectAndSearch` using `usShipmentNbrPK`. |
| 33 | `click` | `css=tr:nth-child(14) > td:nth-child(2)` | No-op focus click. |
| 34 | `click` | `id=MainContent_GetPacketsBtn` | `BdisWorkflow.PacketPrintSelectAndSearch`. |
| 35 | `click` | `id=MainContent_Grid1_Remove_0` | `BdisWorkflow.PacketPrintPrint`. |
| 36 | `click` | `id=MainContent_BtnPrint` | `BdisWorkflow.PacketPrintPrint`. |
| 37 | `selectWindow` | recorded print window | `BdisWorkflow.CloseNewWindowAndReturn`. |
| 38 | `close` | - | `BdisWorkflow.CloseNewWindowAndReturn`. |
| 39 | `selectWindow` | `root` | `BdisWorkflow.CloseNewWindowAndReturn`. |
| 40 | `click` | `id=hlManipulateImage` | `BdisWorkflow.UploadImage`. |
| 41 | `click` | `css=.formbuttonblue` | `BdisWorkflow.UploadImage` fallback only. |
| 42 | `type` | `css=input:nth-child(3)` = `C:\fakepath\Image.tif` | `BdisWorkflow.UploadImage` sends `ImageFilePath`. |
| 43 | `click` | `css=.fa-rotate-left` | `BdisWorkflow.EditImage`. |
| 44 | `click` | `css=.fa-rotate-right` | `BdisWorkflow.EditImage`. |
| 45 | `click` | `css=.fa-search-plus` | `BdisWorkflow.EditImage`. |
| 46 | `click` | `css=.fa-search-minus` | `BdisWorkflow.EditImage`. |
| 47 | `click` | `css=.checkbox-select > .ng-untouched` | `BdisWorkflow.DeleteImage` with checkbox fallback. |
| 48 | `chooseOkOnNextConfirmation` | - | `BdisWorkflow.DeleteImage` waits for the alert. |
| 49 | `click` | `css=.fa-trash` | `BdisWorkflow.DeleteImage`. |
| 50 | `assertConfirmation` | `Are you sure you want to delete the 1 selected image(s)?` | `BdisWorkflow.DeleteImage` validates `alert.Text`. |
| 51 | `webdriverChooseOkOnVisibleConfirmation` | - | `BdisWorkflow.DeleteImage` -> `alert.Accept()`. |
| 52 | `click` | `linkText=Log Out` | `BdisLoginService.Logout`. |
| 53-54 | `type` | `MainContent_ebUserID` / `MainContent_ebPassword` | Recorded preparation for the next login; handled by `BdisLoginService.Login`. |

### 4.3 Intentional deviations from the SIDE recording

- Focus-only clicks (`css=html`, `.bcopyright`, `.content-wrapper`, body cells, password focus clicks) are not replayed because `Click`, `Clear` and `SendKeys` already focus the target and the clicks do not change application state.
- `setWindowSize 1957x1380` is implemented as `Window.Maximize()` to avoid resolution-dependent layout failures.
- Canada is explicitly selected in the rewritten script even though the Canada recording omits the `select` command. This makes the country deterministic and enables the same workflow for every URL.
- The US recording goes directly to `hlPacketPrint` after the email step. The rewritten workflow returns to Document Query first, then goes to Packet Print. This is behaviorally equivalent and more deterministic.
- The SIDE file path `C:\fakepath\Image.tif` is a browser placeholder. The script sends the real configured path `C:\Selenium\BDISWEB-SANDY\BDISImg\Image.tif`.
- The Canada and US file-input selectors differ in the recording (`body > input` vs `input:nth-child(3)`). The script uses `input[type='file']` first and falls back to the upload button, which covers both recordings.
- The delete confirmation is handled by an explicit alert wait and `Accept()`, then the text is validated. This is equivalent to `chooseOkOnNextConfirmation` plus `assertConfirmation`.

## 5. Power Automate compatibility assessment

### 5.1 Existing flow

The documented flow is:

1. Webhook request received.
2. Parse JSON.
3. Initialize an array variable.
4. Apply to each item from the Parse JSON body.
5. Build `FilasTarjetas`.
6. Post an Adaptive Card in Teams.

### 5.2 Observed contract inconsistency

- Requirement section 19 documents a Parse JSON schema whose root is an array of environment rows.
- Requirement section 20 says the Adaptive Card references `testName`, `user` and `globalStatus` from `triggerBody()`, while Parse JSON expects an array.
- The preserved script contract is a top-level object:

```json
{
  "testName": "BDIS",
  "user": "<windows-user>",
  "globalStatus": "PASSED",
  "results": []
}
```

The implementation keeps this object envelope because:

- It preserves the working script contract selected for this refactoring.
- The flow can feed `results` to Parse JSON while still reading `testName`, `user` and `globalStatus` from the trigger body.
- It avoids changing the shared schema or Adaptive Card.

**Validation required:** confirm that the existing flow's Parse JSON action consumes `triggerBody()?['results']` (or an equivalent Compose output). If the flow currently applies Parse JSON directly to the whole trigger body expecting an array, only the flow-side Parse JSON input must change to `results`; the script payload and Adaptive Card remain unchanged. No flow change was made in this refactoring.

### 5.3 Payload format

Success example:

```json
{
  "testName": "BDIS",
  "user": "selenium-runner",
  "globalStatus": "PASSED",
  "results": [
    {
      "webKey": "F5",
      "targetUrl": "https://www.brok.dis.ups.com/login.aspx",
      "status": "PASSED",
      "startTime": "2026-11-04T23:00:00Z",
      "endTime": "2026-11-04T23:08:30Z",
      "error": ""
    }
  ]
}
```

Aggregated country failure example:

```json
{
  "testName": "BDIS",
  "user": "selenium-runner",
  "globalStatus": "FAILED",
  "results": [
    {
      "webKey": "F5",
      "targetUrl": "https://www.brok.dis.ups.com/login.aspx",
      "status": "FAILED",
      "startTime": "2026-11-04T23:00:00Z",
      "endTime": "2026-11-04T23:08:30Z",
      "error": "Canada: PASSED | United States: FAILED at Manipulate Image - Upload because the upload confirmation was not displayed."
    }
  ]
}
```

Rules:

- `webKey` identifies the environment.
- `targetUrl` is the tested URL.
- `status` is `PASSED` only when both Canada and United States pass.
- `startTime` is the start of the environment run.
- `endTime` is the end of both country workflows.
- `startTime` and `endTime` are always UTC ISO 8601 values ending in `Z`.
- `error` contains sanitized, aggregated country failure details.
- `globalStatus` is `PASSED` only when every environment passes.
- `NOT_EXECUTED` is used only in the local HTML report for prerequisite failures and is not sent to Power Automate.

> **Timezone contract:** the flow calls `convertFromUtc(items('Apply_to_each')?['startTime'], 'Mountain Standard Time', ...)`. `convertFromUtc` requires a UTC timestamp. Serializing a local offset such as `-06:00` makes the `Append_to_array_variable` action fail. The script therefore serializes both values with `.ToUniversalTime()` and the `yyyy-MM-dd'T'HH:mm:ss'Z'` format, matching the original working script.

### 5.4 Adaptive Card assessment

No Adaptive Card changes are required. The card continues to use:

- `testName`
- `user`
- `globalStatus`
- `FilasTarjetas`

Country-level detail is aggregated into the environment `error` field, which is the existing contract. If Teams must display separate Canada and US rows later, that is an optional schema enhancement and must be proposed and validated separately.

### 5.5 Compatibility impact on other tests

- No script payload field was renamed or removed.
- No Parse JSON schema or `FilasTarjetas` change was made.
- Other Selenium tests that send the same object envelope are unaffected.
- If another test sends a root array directly, this refactoring does not change that behavior.

### 5.6 Non-fatal email warnings

- The Document Query email step runs on every environment.
- Some environments are served by backends that cannot send mail. The footer may report a different web server than the configured URL, so the email confirmation never arrives.
- `emailWarningEnvironments` lists the environments where an email failure is a non-fatal `WARNING` instead of a hard `FAILED`. The deployed value is `F5,SU6BDISWEB2,SU6BDISWEB3,SU6BDISWEB4`.
- A warning:
  - keeps `CountryTestResult`, `EnvironmentTestResult` and `globalStatus` as `PASSED`;
  - writes `WARNING` to the local log and shows a yellow `Warning` badge in the local HTML report;
  - does not populate the webhook `error` field and is not sent to Power Automate;
  - lets the workflow continue with Packet Print, Manipulate Image and Logout.
- `SU6BDISWEB5` is not listed, so its email failure remains strict.
- `DocumentQueryEmail` always attempts to return to Document Query in a `finally` block, so a failed email cannot leave the email form open and break Packet Print.
- `emailWarningTimeoutSeconds` (default: `pageLoadTimeoutSeconds`) shortens the confirmation wait only on warning environments.
- After login, the effective URL and the `.bcopyright` footer text are written to the local log for diagnostics only. They are not added to the HTML report.

### 5.7 Webhook diagnostics and retries

- The reporter logs the endpoint host, the exact payload and the HTTP status plus response body.
- Transient failures (network errors, timeouts, HTTP 429 and HTTP 5xx) are retried up to 3 times with a short backoff.
- HTML report generation is isolated from webhook delivery: a report failure does not prevent the notification.
- The payload envelope and field names are unchanged.

### 5.8 Dialog foreground activation

- The mode selection, EdgeDriver help and error dialogs set `ShowInTaskbar`, restore the normal window state and call `SetForegroundWindow`, `BringWindowToTop` and `AttachThreadInput`.
- The primary button receives focus when the dialog opens.
- This keeps the dialogs visible when LINQPad is not the active application.

## 6. Manual validation checklist

### Configuration

- [ ] `Config.properties` exists at `C:\Selenium\BDISWEB-SANDY\Config.properties`.
- [ ] `f5Url`, `webServerUrl2..5`, `username`, `password`, `emailRecipient` are present.
- [ ] `caShipmentNbr`, `caShipmentNbrPK`, `usShipmentNbr`, `usShipmentNbrPK` are present.
- [ ] `powerAutomateWebhookUrl` is present if webhook reporting is required.
- [ ] `emailWarningEnvironments` and `emailWarningTimeoutSeconds` are present.
- [ ] `BDISImg\Image.tif` exists.
- [ ] No obsolete keys remain: `url1..url5`, `usrName1..usrPass5`, `usrNameTxtBox`, `usrPassTxtBox`, `loginBtn`, `countryLogin`, `countryCode`, `shipStartDate`, `shipEndDate`, `dutyType`, `mngAcctUser`, `brkrCode`, `newBrokName`.
- [ ] A missing or empty required key stops the run with a configuration error and no browser launch.

### Execution mode

- [ ] Startup dialog offers `F5 / File Server Test` and `Full Test (All URLs)`.
- [ ] The startup dialog opens in the foreground with the F5 button focused.
- [ ] Cancelling the dialog exits without launching Edge.
- [ ] `F5 / File Server Test` runs exactly one environment (`f5Url`) with webKey `F5`.
- [ ] `Full Test` runs five environments: `f5Url`, `webServerUrl2..5` with webKeys `F5`, `SU6BDISWEB2..5`.

### Per environment and country

- [ ] Canada runs first, then United States, for every selected URL.
- [ ] Login uses the universal credentials and the configured country.
- [ ] Country selection is confirmed from the `CountryCode=CA` / `CountryCode=US` link.
- [ ] Document Query searches the configured shipment and prints.
- [ ] Document Query sends the email to the configured recipient and body `BDIS TEST`.
- [ ] On `F5`, `SU6BDISWEB2`, `SU6BDISWEB3` and `SU6BDISWEB4`, an email failure is shown as `Warning`, the workflow continues and the environment stays `PASSED`.
- [ ] On `SU6BDISWEB5`, an email failure is still a hard failure.
- [ ] After an email warning, Packet Print, Manipulate Image and Logout still run.
- [ ] Packet Print selects `Commercial Invoice` for Canada and `CI - Commerical Invoice` for the US.
- [ ] Packet Print searches the configured packet shipment, removes the row and prints.
- [ ] Manipulate Image uploads `Image.tif`, rotates left/right, zooms in/out, deletes with confirmation, and validates the confirmation text.
- [ ] Logout returns to the login page; the next country logs in cleanly.
- [ ] A failure in one country does not prevent the other country from running when the browser is still alive.
- [ ] A dead browser is recreated before the next country.
- [ ] A failed step captures a screenshot when the browser is still available.
- [ ] Login failures do not capture screenshots, so the username field cannot appear in a screenshot.
- [ ] Each environment is `PASSED` only when both countries pass.
- [ ] `globalStatus` is `PASSED` only when every environment passes.

### Reporting

- [ ] `log\BDISWebApplication.txt` is updated with tab-separated step rows.
- [ ] `log\BDISWebApplication_<timestamp>.html` is generated for success, functional failure, and prerequisite failure.
- [ ] Screenshots are saved under `log\`.
- [ ] Prerequisite failures appear as `NOT_EXECUTED` in the local report.
- [ ] Email warnings show a yellow `Warning` badge and are counted in the summary card.
- [ ] Email warnings never appear in the webhook payload.
- [ ] Environment-level failures appear as an `Environment` pseudo-country row in the local report and log.
- [ ] No username, password, webhook URL or recipient appears in the log, HTML report, screenshots, exception text or webhook payload.

### Edge WebDriver

- [ ] Missing `msedgedriver.exe` displays the non-expiring help dialog with the official Edge WebDriver download button.
- [ ] Major-version mismatch between Edge and the driver displays the same help dialog.
- [ ] Driver startup failure stops the entire run and does not send a webhook.
- [ ] The help dialog does not close automatically.

### Power Automate

- [ ] Webhook receives the object envelope with `testName`, `user`, `globalStatus`, `results`.
- [ ] Parse JSON consumes `results` and accepts every required field: `webKey`, `targetUrl`, `status`, `startTime`, `endTime`, `error`.
- [ ] `startTime` and `endTime` are UTC values ending in `Z`.
- [ ] `Append_to_array_variable` and `convertFromUtc` complete without errors.
- [ ] `FilasTarjetas` is built for the environment rows.
- [ ] The Adaptive Card renders and shows the expected status.
- [ ] A failed environment includes the aggregated country failure in `error`.
- [ ] A prerequisite failure sends no webhook.

## 7. Rollback procedure

1. Close LINQPad.
2. Restore the original script:
   - `copy /Y "BDIS\BDISWEB-SANDY\BDIS-SELENIUM.Linq.bak" "C:\Selenium\BDISWEB-SANDY\BDIS-SELENIUM.Linq"`
3. Restore the original configuration:
   - `copy /Y "BDIS\BDISWEB-SANDY\Config.properties.bak" "C:\Selenium\BDISWEB-SANDY\config.properties"`
4. Confirm that `C:\Selenium\BDISWEB-SANDY\BDISImg\Image.tif` still exists.
5. Reopen the original query in LINQPad 8 and run it.
6. No Power Automate, Parse JSON, `FilasTarjetas`, or Adaptive Card rollback is required because none of them were changed.

## 8. Assumptions and unresolved selectors

These items were taken from `BDIS.side` and the existing script but could not be verified against the live application in the current macOS environment:

- The country dropdown accepts `Canada` and `United States` by visible text. `CountrySelectionService` falls back to value `CA` / `US` and partial text.
- The Packet Print labels are exactly `Commercial Invoice` and `CI - Commerical Invoice`. The US spelling is preserved. `SelectPacketByName` falls back to partial text.
- Login success is detected by `hlDocumentQuery` or `tbVal1_1`.
- The email success message is `E-mail successfully sent to <recipient>.` and is used as a wait condition.
- The file input may be present immediately, hidden, or created only after clicking `.formbuttonblue`. `UploadImage` implements direct `SendKeys`, a button fallback, and a JavaScript visibility fallback.
- The delete confirmation is a native JavaScript alert with the exact text `Are you sure you want to delete the 1 selected image(s)?`.
- The image selection control may be `.checkbox-select > .ng-untouched` or `.checkbox-select input[type='checkbox']`; both are supported.
- Print actions open a new window that must be closed and followed by a return to the main handle.
- Logout exposes `lnkbLogOut` or a `Log Out` link.
- `su6bdisweb2..su6bdisweb5` must be resolvable from the test machine (intranet, DNS, VPN, or hosts file).
- LINQPad 8 must support the `<Reference>System.Windows.Forms</Reference>` directive in a Program query. The script intentionally does not use a `#r` directive for this framework assembly.
- The WinForms dialogs require Windows and an STA thread; the current environment is macOS, so live dialog behavior was not executed.
- The existing Power Automate flow consumes the preserved object envelope and reads the `results` array for Parse JSON. This is the one contract item that must be confirmed in the flow before production use.
- The deployed base directory is `C:\Selenium\BDISWEB-SANDY`. The script reads `Config.properties` and `BDISImg\Image.tif` from that location, matching the original deployment layout.
