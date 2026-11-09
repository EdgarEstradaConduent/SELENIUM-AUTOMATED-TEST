# BDIS Selenium Refactoring Notes

## 1. Scope and sources

This refactoring replaces the legacy BDIS test methods with the functional flow recorded in `NEW BDIS.side` while preserving the existing LINQPad infrastructure. The legacy `BDIS.side` file is obsolete and is no longer used as a reference.

| Source | Role |
| --- | --- |
| `BDIS/NEW BDIS.side` | Functional source of truth: order, navigation, selectors, values, country-specific behavior and the conditional email test. |
| `BDIS/BDIS.side` | Obsolete legacy recording. Do not use. |
| `BDIS/BDISWEB-SANDY/BDIS-SELENIUM.Linq` | New implementation. |
| `BDIS/BDISWEB-SANDY/BDIS-SELENIUM.Linq.bak` | Original script for rollback and infrastructure reference. |
| `BDIS/BDISWEB-SANDY/Config.properties` | New configuration. |
| `BDIS/BDISWEB-SANDY/Config.properties.bak` | Original configuration for rollback. |
| `BDIS/UPS Background Removed.png` | Original UPS logo uploaded by the user. |
| `BDIS/BDISWEB-SANDY/ups-logo.png` | Deployed logo used by the dialogs and the HTML report. |
| `BDIS/INSTRUCCIONES.txt` | Refactoring requirements. |

Changed files:

- `BDIS/BDISWEB-SANDY/BDIS-SELENIUM.Linq`
- `BDIS/BDISWEB-SANDY/Config.properties`
- `BDIS/BDISWEB-SANDY/ups-logo.png` (new)
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

# Image uploaded during the Manipulate Images step
imageFilePath=C:\Selenium\BDISWEB-SANDY\BDISImg\Image.tif

# UPS logo used by the dialogs and the HTML report
upsLogoPath=C:\Selenium\BDISWEB-SANDY\ups-logo.png

# Recipient used by the Document Query email step.
# NEW BDIS.side runs this step only when the page text reports SU6BDISWEB5.
emailRecipient=<recipient>

# Power Automate webhook endpoint
powerAutomateWebhookUrl=<webhook-url>

# Proxy used only for the Power Automate webhook request.
# Leave webhookProxyHost empty to use a direct connection.
webhookProxyHost=10.138.112.254
webhookProxyPort=9191

# Selenium timeouts in seconds
# Defaults used when a value is absent: 30, 60 and 120 respectively.
elementTimeoutSeconds=30
pageLoadTimeoutSeconds=60
uploadTimeoutSeconds=120

# Pause after the image upload is confirmed and before the rotation commands.
imageUploadPauseSeconds=60

# Pause before closing each print preview window (NEW BDIS.side waits 30s).
printWindowPauseSeconds=30
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
| `caShipmentNbr` | `caShipmentNbr` | Retained for Canada Document Query; value updated from `NEW BDIS.side`. |
| `usShipmentNbr` | `usShipmentNbr` | Retained for US Document Query; value updated from `NEW BDIS.side`. |
| n/a | `caShipmentNbrPK` | Canada Packet Print shipment from `NEW BDIS.side`. |
| n/a | `usShipmentNbrPK` | US Packet Print shipment from `NEW BDIS.side`. |
| n/a | `emailRecipient` | Recipient used by the conditional email step from `NEW BDIS.side`. |
| n/a | `elementTimeoutSeconds` | Explicit element wait timeout. |
| n/a | `pageLoadTimeoutSeconds` | Explicit page load timeout. |
| n/a | `uploadTimeoutSeconds` | Explicit image upload timeout. |
| n/a | `imageFilePath` | Absolute or relative path of the image uploaded by Manipulate Images. Relative paths resolve against the base directory. |
| n/a | `upsLogoPath` | Absolute or relative path of the UPS logo used by the dialogs and the HTML report. Relative paths resolve against the base directory. |
| n/a | `imageUploadPauseSeconds` | Pause after upload and before rotating/zooming the image. |
| n/a | `printWindowPauseSeconds` | Pause before closing each print preview window. |
| n/a | `webhookProxyHost` | Proxy host for the webhook request; empty means direct. |
| n/a | `webhookProxyPort` | Proxy port for the webhook request. |

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
| `HtmlReportGenerator` | UPS-branded HTML dashboard with a `SKIPPED` badge; embeds the logo read from `upsLogoPath`; no target URL column or footer. |
| `UpsBranding` | Shared UPS colors, configurable logo loading with embedded Base64 fallback, and WinForms dialog styling. |
| `PowerAutomateReporter` | Builds and posts the existing object envelope; optional webhook proxy. |
| `JsonHelper` | JSON escaping for the manually built payload. |
| `ScreenshotHelper` | Failure screenshots. |
| `SeleniumWaitHelper` | Explicit waits, alerts, page text and new-window handling. |
| `CountrySelectionService` | Selects the country; no Packet Print href assertion. |
| `BdisLoginService` | Login and logout for each target URL. |
| `BdisWorkflow` | Document Query, Packet Print and Manipulate Image workflow. |
| `EdgeDriverManager` | Driver preflight version check and Edge creation. |
| `DialogService` | Mode selection, non-expiring EdgeDriver help, error dialogs. |
| `BdisTestRunner` | Orchestrates environments, countries, steps and aggregation. |
| `Main` | Entry point; prerequisite failures; report and webhook dispatch. |

Removed legacy test methods:

`Login`, `DocumentQuery`, `WebStatus`, `PacketPrint`, `DocType`, `UploadDocument`, `AccountManagement`, `ManipulateImage`.

These are replaced by `BdisLoginService`, `BdisWorkflow`, and `BdisTestRunner`. The infrastructure they used (evaluation, timing, error capture, screenshots, log, HTML report, environment status, global status, Power Automate payload) is preserved.

## 4. `NEW BDIS.side` command mapping

The command numbers below are the exact indexes recorded in `NEW BDIS.side`. The legacy `BDIS.side` numbering is no longer used.

### 4.1 CA-Full Test

| # | SIDE command | Target / value | C# implementation |
| --- | --- | --- | --- |
| 0 | `setWindowSize` | `1400x900` | `BdisTestRunner.ApplyWindowSize` sets `driver.Manage().Window.Size = new Size(1400, 900)`. |
| 1 | `open` | `https://www.brok.dis.ups.com/login.aspx` | `BdisLoginService.Login` -> `driver.Navigate().GoToUrl(url)` using `f5Url`. |
| 2-3 | `click` / `type` | `id=MainContent_ebUserID` | `BdisLoginService.Login` -> `Click`, `Clear`, `SendKeys(username)`. |
| 4-5 | `click` / `type` | `id=MainContent_ebPassword` | `BdisLoginService.Login` -> `Click`, `Clear`, `SendKeys(password)`. |
| 6 | `click` | `css=tr:nth-child(10) > td` | No-op focus click; not replayed. |
| 7 | `click` | `id=MainContent_ddlCountry` | `CountrySelectionService.SelectCountry` selects `Canada` deterministically even though the recording omits an explicit `select` for CA. |
| 8 | `click` | `id=MainContent_btnLogin` | `BdisLoginService.Login` -> `WaitClickable(...).Click()`. |
| 9-10 | `click` / `type` | `id=tbVal1_1` = `121429J4K3H` | `BdisWorkflow.DocumentQuerySearch` -> `Click`, `Clear`, `SendKeys(caShipmentNbr)`. |
| 11 | `click` | `id=btnSearch` | `BdisWorkflow.DocumentQuerySearch`. |
| 12 | `click` | `id=MainContent_Checkbox1` | `BdisWorkflow.DocumentQueryPrint`. |
| 13 | `click` | `id=MainContent_btnPrint` | `BdisWorkflow.DocumentQueryPrint`. |
| 14 | `storeWindowHandle` | `root` | `BdisWorkflow.DocumentQueryPrint` records `driver.CurrentWindowHandle`. |
| 15 | `selectWindow` | recorded print window | `BdisWorkflow.CloseNewWindowAndReturn` switches to the new handle. |
| 16 | `pause` | `30000` | `CloseNewWindowAndReturn` sleeps `printWindowPauseSeconds` (default 30). |
| 17 | `close` | - | `CloseNewWindowAndReturn` -> `driver.Close()`. |
| 18 | `selectWindow` | `${root}` | `CloseNewWindowAndReturn` switches back to the main handle. |
| 19 | `executeScript` | web server detection over `document.body.innerText` | `BdisWorkflow.DetectWebServer`. |
| 20 | `echo` | `Server Detected "${webServer}"` | `BdisTestRunner` writes the detected server to the local log only. |
| 21 | `if` | `document.body.innerText...includes("SU6BDISWEB5")` | `BdisWorkflow.IsEmailRequired` gates the email step. |
| 22-27 | `click` / `type` | email form (`MainContent_btnEmail`, `txtEmailTo`, `txtEmailBody`, `BtnSend`) | `BdisWorkflow.DocumentQueryEmail`; body text is `BDIS E-mail Test`. |
| 28 | `assertText` | `id=MainContent_lblMsg` = `E-mail successfully sent to <recipient>.` | `DocumentQueryEmail` waits for page source to contain the confirmation. |
| 29-31 | `else` / `echo` / `end` | email skipped | `BdisTestRunner.RecordSkippedStep` writes a `SKIPPED` step; the workflow continues. |
| 32 | `click` | `id=hlPacketPrint` | `BdisWorkflow.PacketPrintSelectAndSearch`. |
| 33-34 | `click` / `select` | `id=MainContent_PacketName` = `label=Commercial Invoice` | `PacketPrintSelectAndSearch` clicks the list, waits for the postback to populate it, then `SelectPacketByName` chooses `CountryTestProfile.PacketNameLabel`. |
| 35 | `type` | `id=MainContent_Box1` = `782717VVJ4B` | `BdisWorkflow.PacketPrintSelectAndSearch` using `caShipmentNbrPK`. |
| 36 | `click` | `id=MainContent_GetPacketsBtn` | `BdisWorkflow.PacketPrintSelectAndSearch`. |
| 37 | `click` | `id=MainContent_Grid1_Remove_0` | `BdisWorkflow.PacketPrintPrint`. |
| 38 | `click` | `id=MainContent_BtnPrint` | `BdisWorkflow.PacketPrintPrint`. |
| 39 | `selectWindow` | recorded print window | `BdisWorkflow.CloseNewWindowAndReturn`. |
| 40 | `pause` | `30000` | `CloseNewWindowAndReturn` sleeps `printWindowPauseSeconds`. |
| 41 | `close` | - | `CloseNewWindowAndReturn`. |
| 42 | `selectWindow` | `${root}` | `CloseNewWindowAndReturn`. |
| 43 | `click` | `id=hlManipulateImage` | `BdisWorkflow.UploadImage`. |
| 44 | `click` | `css=.formbuttonblue` | `BdisWorkflow.UploadImage` fallback only; the primary path targets the file input directly. |
| 45 | `type` | `css=body > input` = `...\BDISImg` | `BdisWorkflow.UploadImage` attaches `ImageFilePath` (`BDISImg\Image.tif`) through CDP. The recording points to the folder; the script uses the file. |
| 46 | `click` | `css=.fa-rotate-left` | `BdisWorkflow.EditImage`. |
| 47 | `click` | `css=.fa-rotate-right` | `BdisWorkflow.EditImage`. |
| 48 | `click` | `css=.fa-search-plus` | `BdisWorkflow.EditImage`. |
| 49 | `click` | `css=.fa-search-minus` | `BdisWorkflow.EditImage`. |
| 50 | `click` | `css=.checkbox-select > .ng-untouched` | `BdisWorkflow.DeleteImage` tries `.ng-untouched`, then falls back to `.checkbox-select input[type='checkbox']`. |
| 51 | `click` | `css=.fa-trash` | `BdisWorkflow.DeleteImage`. |
| 52 | `chooseOkOnNextConfirmation` | - | `BdisWorkflow.DeleteImage` waits for the alert. |
| 53 | `assertConfirmation` | `Are you sure you want to delete the 1 selected image(s)?` | `BdisWorkflow.DeleteImage` validates `alert.Text` before accepting. |
| 54 | `webdriverChooseOkOnVisibleConfirmation` | - | `BdisWorkflow.DeleteImage` -> `alert.Accept()`. |
| 55 | `click` | `linkText=Log Out` | `BdisLoginService.Logout` prefers `lnkbLogOut`, then link text `Log Out`. |

### 4.2 USA-Full Test

| # | SIDE command | Target / value | C# implementation |
| --- | --- | --- | --- |
| 0 | `setWindowSize` | `1400x900` | `BdisTestRunner.ApplyWindowSize` sets `driver.Manage().Window.Size = new Size(1400, 900)`. |
| 1 | `open` | `https://www.brok.dis.ups.com/login.aspx` | `BdisLoginService.Login` using `f5Url`. |
| 2-3 | `click` / `type` | `id=MainContent_ebUserID` | `BdisLoginService.Login`. |
| 4-5 | `click` / `type` | `id=MainContent_ebPassword` | `BdisLoginService.Login`. |
| 6 | `click` | `css=tr:nth-child(10) > td` | No-op focus click; not replayed. |
| 7-8 | `click` / `select` | `id=MainContent_ddlCountry` = `label=United States` | `CountrySelectionService.SelectCountry` using `CountrySelectText`. |
| 9 | `click` | `id=MainContent_btnLogin` | `BdisLoginService.Login`. |
| 10-11 | `click` / `type` | `id=tbVal1_1` = `00R701G3FLT` | `BdisWorkflow.DocumentQuerySearch` using `usShipmentNbr`. |
| 12 | `click` | `id=btnSearch` | `BdisWorkflow.DocumentQuerySearch`. |
| 13 | `click` | `id=MainContent_Checkbox1` | `BdisWorkflow.DocumentQueryPrint`. |
| 14 | `click` | `id=MainContent_btnPrint` | `BdisWorkflow.DocumentQueryPrint`. |
| 15 | `storeWindowHandle` | `root` | `BdisWorkflow.DocumentQueryPrint`. |
| 16 | `selectWindow` | recorded print window | `BdisWorkflow.CloseNewWindowAndReturn`. |
| 17 | `pause` | `30000` | `CloseNewWindowAndReturn` sleeps `printWindowPauseSeconds`. |
| 18 | `close` | - | `CloseNewWindowAndReturn`. |
| 19 | `selectWindow` | `${root}` | `CloseNewWindowAndReturn`. |
| 20 | `executeScript` | web server detection | `BdisWorkflow.DetectWebServer`. |
| 21 | `echo` | `Server Detected "${webServer}"` | Local log only. |
| 22 | `if` | `...includes("SU6BDISWEB5")` | `BdisWorkflow.IsEmailRequired`. |
| 23-28 | `click` / `type` | email form | `BdisWorkflow.DocumentQueryEmail`; body `BDIS E-mail Test`. |
| 29 | `assertText` | `id=MainContent_lblMsg` | `DocumentQueryEmail` wait condition. |
| 30-32 | `else` / `echo` / `end` | email skipped | `BdisTestRunner.RecordSkippedStep` (`SKIPPED`). |
| 33 | `click` | `id=hlPacketPrint` | `BdisWorkflow.PacketPrintSelectAndSearch`. |
| 34-35 | `click` / `select` | `id=MainContent_PacketName` = `label=CI - Commerical Invoice` | `PacketPrintSelectAndSearch` clicks the list, waits for the postback, then `SelectPacketByName`. Matching normalizes `Commerical`/`Commercial`. |
| 36 | `type` | `id=MainContent_Box1` = `A20743KT8MJ` | `BdisWorkflow.PacketPrintSelectAndSearch` using `usShipmentNbrPK`. |
| 37 | `click` | `id=MainContent_GetPacketsBtn` | `BdisWorkflow.PacketPrintSelectAndSearch`. |
| 38 | `click` | `id=MainContent_Grid1_Remove_0` | `BdisWorkflow.PacketPrintPrint`. |
| 39 | `click` | `id=MainContent_BtnPrint` | `BdisWorkflow.PacketPrintPrint`. |
| 40 | `selectWindow` | recorded print window | `CloseNewWindowAndReturn`. |
| 41 | `pause` | `30000` | `CloseNewWindowAndReturn` sleeps `printWindowPauseSeconds`. |
| 42 | `close` | - | `CloseNewWindowAndReturn`. |
| 43 | `selectWindow` | `${root}` | `CloseNewWindowAndReturn`. |
| 44 | `click` | `id=hlManipulateImage` | `BdisWorkflow.UploadImage`. |
| 45 | `click` | `css=.formbuttonblue` | `BdisWorkflow.UploadImage` fallback only. |
| 46 | `type` | `css=body > input` = `...\BDISImg` | `BdisWorkflow.UploadImage` attaches `ImageFilePath` through CDP. |
| 47-50 | `click` | `.fa-rotate-left`, `.fa-rotate-right`, `.fa-search-plus`, `.fa-search-minus` | `BdisWorkflow.EditImage`; no preview-image click. |
| 51 | `click` | `css=.checkbox-select > .ng-untouched` | `BdisWorkflow.DeleteImage` with checkbox fallback. |
| 52 | `click` | `css=.fa-trash` | `BdisWorkflow.DeleteImage`. |
| 53 | `chooseOkOnNextConfirmation` | - | `BdisWorkflow.DeleteImage` waits for the alert. |
| 54 | `assertConfirmation` | `Are you sure you want to delete the 1 selected image(s)?` | `BdisWorkflow.DeleteImage` validates `alert.Text`. |
| 55 | `webdriverChooseOkOnVisibleConfirmation` | - | `BdisWorkflow.DeleteImage` -> `alert.Accept()`. |
| 56 | `click` | `linkText=Log Out` | `BdisLoginService.Logout`. |

### 4.3 Intentional deviations from `NEW BDIS.side`

- Focus-only clicks (`css=tr:nth-child(10) > td`) are not replayed because `Click`, `Clear` and `SendKeys` already focus the target and the click does not change application state.
- `setWindowSize 1400x900` is implemented exactly as recorded; `Maximize()` is no longer used.
- Canada is explicitly selected even though the CA recording omits the `select` command. This makes the country deterministic and lets the same workflow run for every URL.
- The recorded `pause 30000` before closing a print preview is implemented as `printWindowPauseSeconds`; the default is 30 seconds.
- `NEW BDIS.side` no longer clicks `.iv-viewer > img` before rotating. The script only waits for the preview to be visible.
- The image `type` target in the recording points to the `BDISImg` folder. The script sends the real file path `C:\Selenium\BDISWEB-SANDY\BDISImg\Image.tif`.
- Selenium IDE reports `file uploading is only supported in chrome at this time` for `type` on a file input. The script bypasses that limitation with CDP `DOM.setFileInputFiles`, then falls back to `SendKeys` and a JavaScript `DataTransfer`.
- The email step is not skipped by configuration. It is skipped only when the page text does not report `SU6BDISWEB5`, exactly like the `if` command in the recording.
- The delete confirmation is handled by an explicit alert wait and `Accept()`, then the text is validated. This is equivalent to `chooseOkOnNextConfirmation` plus `assertConfirmation`.


## 5. Behavior and Power Automate compatibility

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

### 5.6 Conditional email test

- `NEW BDIS.side` does not run the email test on every environment. After the print preview closes, it detects the web server from `document.body.innerText` using the list `SU6BDISWEB2`, `SU6BDISWEB3`, `SU6BDISWEB4`, `SU6BDISWEB5`.
- The email step runs only when the detected server is `SU6BDISWEB5`, matching the recording's `if` condition.
- On every other server (including `F5` and `UNKNOWN`) the step is recorded as `SKIPPED`:
  - it keeps `CountryTestResult`, `EnvironmentTestResult` and `globalStatus` unchanged;
  - it is written to the local log and shown with a gray `SKIPPED` badge in the HTML report;
  - it is not added to the webhook `error` field and is not an error notification;
  - the workflow continues with Packet Print, Manipulate Image and Logout.
- There is no `emailWarningEnvironments` or `emailWarningTimeoutSeconds` setting anymore. The old optional-step/WARNING mechanism was removed, so the email step is now either executed and strict, or intentionally skipped.
- The email body is `BDIS E-mail Test`, matching `NEW BDIS.side`.
- `DocumentQueryEmail` always attempts to return to Document Query in a `finally` block, so an executed email step cannot leave the email form open and break Packet Print.
- After login, the effective URL and the `.bcopyright` footer text are written to the local log for diagnostics only. They are not added to the HTML report.

### 5.7 Edge image upload

- The SIDE `type` command on a file input fails in Microsoft Edge with `file uploading is only supported in chrome at this time`. That message comes from Selenium IDE, not from the browser driver.
- `UploadImage` therefore uses the Chromium DevTools Protocol directly:
  1. `Page.enable` and `Page.setInterceptFileChooserDialog` to prevent the native Windows dialog from blocking the session.
  2. `DOM.getDocument` and `DOM.querySelector` to resolve the `input[type='file']` node.
  3. `DOM.setFileInputFiles` to attach `Image.tif` without automating the native dialog.
- Fallbacks, in order: `WebElement.SendKeys` on a temporarily visible input, then a JavaScript `DataTransfer` assignment with `input`/`change` events.
- The preview `.iv-viewer > img` must be visible before continuing.
- A configurable pause (`imageUploadPauseSeconds`, default 60) runs after the upload is confirmed and before the rotation/zoom commands, so the image widget can finish processing.
- Browser policy is unchanged: the test still runs in Microsoft Edge.

### 5.8 Webhook diagnostics, retries and proxy

- The reporter logs the endpoint host, the exact payload and the HTTP status plus response body.
- Transient failures (network errors, timeouts, HTTP 429 and HTTP 5xx) are retried up to 3 times with a short backoff.
- `webhookProxyHost` and `webhookProxyPort` configure a proxy that applies only to the webhook HTTP request. When the keys are absent, the defaults are `10.138.112.254` and `9191`. An explicitly empty `webhookProxyHost` uses a direct connection.
- The proxy is not applied to the browser session and does not change how the target site is reached.
- HTML report generation is isolated from webhook delivery: a report failure does not prevent the notification.
- The payload envelope and field names are unchanged.

### 5.9 Dialog foreground activation and UPS branding

- The mode selection, EdgeDriver help and error dialogs set `ShowInTaskbar`, restore the normal window state and call `SetForegroundWindow`, `BringWindowToTop` and `AttachThreadInput`.
- The primary button receives focus when the dialog opens.
- This keeps the dialogs visible when LINQPad is not the active application.
- All three dialogs use the shared `UpsBranding` style: brown `#351C15` header, gold `#FFB500` accent and border, light `#F5F3F0` background, and the UPS logo in the top-right corner.
- Primary and secondary buttons use the same brown/gold palette.
- The logo is read from `upsLogoPath`. The dialogs load it through `UpsBranding.LoadLogo()` and the HTML report embeds it as a Base64 data URI, so each report remains a single self-contained file.
- If `upsLogoPath` is missing or unreadable, both the dialogs and the report fall back to the embedded Base64 PNG. A warning is written to the local log.
- The dialog logo sits in a white panel docked to the right of the brown header so the UPS mark is fully visible against the branding.

### 5.10 Packet Print and print-preview handling

- Packet Print reproduces the recorded sequence exactly: click `hlPacketPrint`, click `MainContent_PacketName`, wait for the postback to populate the option list, select the packet, type the shipment number, click `GetPacketsBtn`, remove row 0 and click `MainContent_BtnPrint`.
- `SelectPacketByName` waits until the requested packet option is actually present, resolves its index on a fresh `SelectElement`, selects by index and verifies the selected text through a new lookup. Waiting for the option (not just any option) avoids selecting from a stale list, and the fresh lookups avoid the stale-reference failure observed after the postback.
- Matching first tries the normalized exact label, then a normalized partial match. `Commerical` and `Commercial` are treated as the same word, so the recorded US label still resolves if the live application uses the other spelling. When nothing matches, the error lists the available options.
- Print previews are detected by comparing the current handle set with the set captured before the print click. The new handle must not be the main window handle. After the pause, the preview is closed and the driver switches back to the main handle, which is verified to still exist.

## 6. Manual validation checklist

### Configuration

- [ ] `Config.properties` exists at `C:\Selenium\BDISWEB-SANDY\Config.properties`.
- [ ] `f5Url`, `webServerUrl2..5`, `username`, `password`, `emailRecipient` are present.
- [ ] `caShipmentNbr`, `caShipmentNbrPK`, `usShipmentNbr`, `usShipmentNbrPK` are present.
- [ ] `powerAutomateWebhookUrl` is present if webhook reporting is required.
- [ ] `imageUploadPauseSeconds` (default 60) and `printWindowPauseSeconds` (default 30) are present or intentionally omitted to use defaults.
- [ ] `webhookProxyHost` and `webhookProxyPort` are set correctly, or the host is empty to use a direct connection.
- [ ] `emailWarningEnvironments` and `emailWarningTimeoutSeconds` are absent.
- [ ] `imageFilePath` points to an existing image (`BDISImg\Image.tif` by default).
- [ ] `upsLogoPath` points to an existing PNG (`ups-logo.png` by default).
- [ ] No obsolete keys remain: `url1..url5`, `usrName1..usrPass5`, `usrNameTxtBox`, `usrPassTxtBox`, `loginBtn`, `countryLogin`, `countryCode`, `shipStartDate`, `shipEndDate`, `dutyType`, `mngAcctUser`, `brkrCode`, `newBrokName`.
- [ ] A missing or empty required key stops the run with a configuration error and no browser launch.

### Execution mode

- [ ] Startup dialog offers `F5 / File Server Test` and `Full Test (All URLs)`.
- [ ] The startup dialog shows the UPS header, the configured logo in a white panel on the right and the brown/gold border.
- [ ] The startup dialog opens in the foreground with the F5 button focused.
- [ ] Cancelling the dialog exits without launching Edge.
- [ ] `F5 / File Server Test` runs exactly one environment (`f5Url`) with webKey `F5`.
- [ ] `Full Test` runs five environments: `f5Url`, `webServerUrl2..5` with webKeys `F5`, `SU6BDISWEB2..5`.
- [ ] The EdgeDriver help and error dialogs also show the UPS header and logo.

### Per environment and country

- [ ] Canada runs first, then United States, for every selected URL.
- [ ] Login uses the universal credentials and the configured country.
- [ ] After login, the detected web server is logged locally.
- [ ] When the page reports `SU6BDISWEB5`, the email step runs with body `BDIS E-mail Test` and expects `E-mail successfully sent to <recipient>.`
- [ ] When the page does not report `SU6BDISWEB5`, the email step is logged and shown as `SKIPPED` and the workflow continues.
- [ ] A skipped email never changes the environment or global status and never appears in the webhook `error` field.
- [ ] Document Query searches the configured shipment and prints.
- [ ] Each print preview stays open for about 30 seconds before it is closed.
- [ ] Packet Print selects `Commercial Invoice` for Canada and `CI - Commerical Invoice` for the US.
- [ ] Packet Print searches the configured packet shipment, removes the row and prints.
- [ ] Manipulate Image uploads `Image.tif` in Microsoft Edge without a native file dialog.
- [ ] The image preview is visible before the rotation commands.
- [ ] The script waits about 60 seconds after the upload and before rotating/zooming.
- [ ] Rotation and zoom controls work without clicking the preview image first.
- [ ] The image is deleted, the confirmation text is validated and the alert is accepted.
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
- [ ] The HTML report shows the configured UPS logo, brown header and gold accents.
- [ ] Removing or renaming the configured logo falls back to the embedded logo without failing the report.
- [ ] The HTML report has no target URL column and no target URL footer.
- [ ] Prerequisite failures appear as `NOT_EXECUTED` in the local report.
- [ ] Skipped email steps show a gray `SKIPPED` badge and are counted in the summary card.
- [ ] Skipped email steps never appear in the webhook payload.
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
- [ ] When `webhookProxyHost` is set, the request is delivered through `webhookProxyHost:webhookProxyPort`.
- [ ] When `webhookProxyHost` is empty, the request uses a direct connection.
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

These items were taken from `NEW BDIS.side` and the existing script but could not be verified against the live application in the current macOS environment:

- The country dropdown accepts `Canada` and `United States` by visible text. `CountrySelectionService` falls back to value `CA` / `US` and partial text.
- The Packet Print labels are `Commercial Invoice` and `CI - Commerical Invoice` in `NEW BDIS.side`. The live list is read after the click postback; matching normalizes `Commerical`/`Commercial` and then falls back to partial text.
- Login success is detected by `hlDocumentQuery` or `tbVal1_1`.
- The email success message is `E-mail successfully sent to <recipient>.` and is used as a wait condition.
- Web server detection reads `document.body.innerText`. If the text cannot be read, the server is reported as `UNKNOWN` and the email step is skipped.
- The file input may be present immediately, hidden, or created only after clicking `.formbuttonblue`. `UploadImage` uses CDP `DOM.setFileInputFiles`, then `SendKeys`, then a JavaScript `DataTransfer` fallback.
- CDP file upload requires a Chromium-based driver (Microsoft Edge). The DevTools session is obtained from `EdgeDriver.GetDevToolsSession()`.
- The 60-second image pause and the 30-second print-preview pause make both countries slower. They are configurable through `imageUploadPauseSeconds` and `printWindowPauseSeconds`.
- The delete confirmation is a native JavaScript alert with the exact text `Are you sure you want to delete the 1 selected image(s)?`.
- The image selection control may be `.checkbox-select > .ng-untouched` or `.checkbox-select input[type='checkbox']`; both are supported.
- Print actions open a new window that must be closed and followed by a return to the main handle.
- Logout exposes `lnkbLogOut` or a `Log Out` link.
- `su6bdisweb2..su6bdisweb5` must be resolvable from the test machine (intranet, DNS, VPN, or hosts file).
- The webhook proxy must be reachable from the test machine. A wrong host or port makes the notification fail after the retry loop; the test result itself is unaffected.
- LINQPad 8 must support the `<Reference>System.Windows.Forms</Reference>` directive in a Program query. The script intentionally does not use a `#r` directive for this framework assembly.
- The WinForms dialogs require Windows and an STA thread; the current environment is macOS, so live dialog behavior was not executed.
- The UPS logo is read from `upsLogoPath` (default `C:\Selenium\BDISWEB-SANDY\ups-logo.png`) and falls back to an embedded Base64 PNG derived from the official mark. Rendering was checked as an image asset, but the WinForms dialogs were not executed on Windows.
- The existing Power Automate flow consumes the preserved object envelope and reads the `results` array for Parse JSON. This is the one contract item that must be confirmed in the flow before production use.
- The deployed base directory is `C:\Selenium\BDISWEB-SANDY`. The script reads `Config.properties`, `BDISImg\Image.tif` and `ups-logo.png` from that location by default, matching the original deployment layout. `imageFilePath` and `upsLogoPath` can override both asset paths.
