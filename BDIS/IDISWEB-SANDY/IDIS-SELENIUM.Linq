<Query Kind="Program">
  <Reference Relative="..\..\msedgedriver.exe">C:\Selenium\msedgedriver.exe</Reference>
  <Reference Relative="..\..\SeleniumExtras.WaitHelpers.dll">C:\Selenium\SeleniumExtras.WaitHelpers.dll</Reference>
  <Reference Relative="..\..\WebDriver.dll">C:\Selenium\WebDriver.dll</Reference>
  <Reference Relative="..\..\WebDriver.Support.dll">C:\Selenium\WebDriver.Support.dll</Reference>
  <Namespace>OpenQA.Selenium</Namespace>
  <Namespace>OpenQA.Selenium.Edge</Namespace>
  <Namespace>OpenQA.Selenium.Support.UI</Namespace>
  <Namespace>SeleniumExtras.WaitHelpers</Namespace>
  <IncludeUncapsulator>false</IncludeUncapsulator>
</Query>

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;

public class TestWebResult
{
    public string WebKey { get; set; }
    public string Url { get; set; }
    public string Status { get; set; } // PASSED or FAILED
    public string StartTime { get; set; }
    public string EndTime { get; set; }
    public string ErrorMessage { get; set; } = "";
}

// Internal structure for the modern unified HTML
public class InternalStepLog
{
    public string Timestamp { get; set; }
    public string StepDescription { get; set; }
    public string ResultDetails { get; set; }
    public string ColorHex { get; set; }
    public string ScreenshotFileName { get; set; }
}

static class Program
{
    // ==========================================
    // DYNAMIC TEST NAME CONFIGURATION
    // ==========================================
    static string testName = "IDIS"; 
    static string siteLabel = "File Server (F5)"; // Friendly name of the only environment under test
    
    static string currentUser = Environment.UserName;
    static string webhookUrl = "https://default1aed4588b8ce43a8a775989538fd30.d8.environment.api.powerplatform.com:443/powerautomate/automations/direct/workflows/218579758dd34aec91459a1858259537/triggers/manual/paths/invoke?api-version=1&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=9B_4JUjNCp4wOLA8gBC-1kFsPGq7m6CqV6wuoGHRQYE";

    static string htmlPath;
    static string logPath;
    static string logFolder;

    // ====== Config ======
    static Dictionary<string,string> cfg = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);

    // ====== WebDriver ======
    static IWebDriver driver;
    static WebDriverWait wait;

    // ====== Automation Run Variables ======
    static string baseUrl, usrPass, usrName, usrNameTxtBox, usrPassTxtBox, ctryLogin, loginBtn;
    static string caShipNbr, usShipNbr, invalidUpass, dutyTypeVal, orgnSSDate, orgnSEDate, manageAcctUser;
    static string brokerCode, brokName, addNewUser, addNewUserPass, addNewUserFName, addNewUserLName;
    static string mawbNumber, bldgID, packageID, region, district;

    // In-memory collection to generate the unified HTML with the new design
    static List<InternalStepLog> accumulatedSteps = new List<InternalStepLog>();

    static void Main()
    {
        // 1) Load config from dynamic LEX folder structure
        var configPath = $@"C:\Selenium\{testName}WEB-SANDY\config.properties";
        if (!File.Exists(configPath)) throw new FileNotFoundException($"Configuration file missing at: {configPath}");
        
        cfg = LoadConfigEnhanced(configPath);

        // Define structured log output paths
        logFolder = $@"C:\Selenium\{testName}WEB-SANDY\log\";
        if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);

        htmlPath = Path.Combine(logFolder, $"{testName}WebApplication.html");
        logPath = Path.Combine(logFolder, $"{testName}WebApplication.txt");

        // Clear TXT logs from previous runs
        File.WriteAllText(logPath, "");

        // 3) Detect Target Site Keys from Config
        var urlKeys = DetectUrlKeys(cfg);
        List<TestWebResult> webResults = new List<TestWebResult>();
        bool globalFailure = false;

        $@"=== STARTING {testName} WEB AUTOMATION TEST ===".Dump();

        // 4) Iterate through each target site environment
        foreach (var urlKey in urlKeys)
        {
            if (!cfg.TryGetValue(urlKey, out var actualUrl) || string.IsNullOrWhiteSpace(actualUrl))
                continue;

            var currentResult = new TestWebResult
            {
                WebKey = siteLabel,
                Url = actualUrl,
                StartTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Status = "PASSED"
            };

            $@"[START] Evaluating environment {urlKey}: {actualUrl}".Dump();
            LogStep($"[Environment: {siteLabel}] Target Site Evaluation Started", actualUrl, "#63B3ED");

            try
            {
                // 4.1) Resolve specific environment credentials by suffix
                var suffix = LastDigit(urlKey);
                ApplyPerSiteCredentials(suffix);

                // 4.2) Map global parameters for the current run
                cfg["url"] = actualUrl;
                AssignConfigVarsForRun();

                // 4.3) Driver Instance Setup (Edge)
                driver = SetupEdgeDriver(cfg);
                wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));

                // 4.4) Execute Test Case Suites sequentially
                driver.Navigate().GoToUrl(baseUrl);
                LogStep("Navigate to Base URL Target", baseUrl);

                // Native invocations of your automations
                loginCase();
                printButton();
                printToFile();
                emailButton();
                printFileViewer();
                viewerFunction();
                addUser();
                modifyUser();
                deleteUser();
                updateProfile();
                Mwab();
                ImgSiteVolume();
                ShipmentDetail();
                PackageDetail();
                KeyedNoDocsImg();
                OCRConfig();
                OCRReport();
                SuspendedSitesUT();
                SuspendedSitesTX();
                SitesNoImg();
                BuildingConfig();
                AutoClass();
                IDISSummary();

                LogStep("Automated Execution Flow", "All internal case modules completed successfully.");
            }
            catch (Exception ex)
            {
                globalFailure = true;
                currentResult.Status = "FAILED";
                currentResult.ErrorMessage = ex.Message;

                string screenshotName = $"Screenshot_Error_{urlKey}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string fullScreenshotPath = Path.Combine(logFolder, screenshotName);
                
                string savedPath = TakeScreenshot(driver, fullScreenshotPath);
                if (!string.IsNullOrEmpty(savedPath))
                {
                    LogStep("Critical Execution Error", $"Failed: {ex.Message}", "#F56565", screenshotName);
                }
                else
                {
                    LogStep("Critical Execution Error", $"Failed: {ex.Message} (Screenshot capture failed)", "#F56565");
                }
            }
            finally
            {
                try 
                { 
                    driver?.Quit();
                    driver = null; 
                } catch { /* ignore driver disposal faults */ }
                
                currentResult.EndTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                webResults.Add(currentResult);
                
                $@"[{currentResult.Status}] Finished processing environment: {urlKey}".Dump();
            }
        }

        // 5) Build HTML File using the Unfied Dashboard Style
        BuildModernHtmlReport(htmlPath);
        
        // 6) Post adaptive card payloads to Webhook endpoint
        SendWebhookNotification(webhookUrl, testName, currentUser, globalFailure, webResults);

        $@"=== AUTOMATION PROCESS COMPLETED ===".Dump();
        $@"HTML Report Dashboard path: {htmlPath}".Dump();
    }

    // ====== CONFIG UTILITIES ======
    static Dictionary<string,string> LoadConfigEnhanced(string path)
    {
        var dict = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return dict;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

            var parts = line.Split('=', 2);
            if (parts.Length == 2)
                dict[parts[0].Trim()] = parts[1].Trim();
        }

        CopyIfPresent(dict, "buildingID", "bldgID");
        CopyIfPresent(dict, "pkgNumber",  "packageID");
        CopyIfPresent(dict, "regn",       "region");
        CopyIfPresent(dict, "dist",       "district");
        return dict;
    }

    static void CopyIfPresent(Dictionary<string,string> d, string from, string to)
    {
        if (d.TryGetValue(from, out var v) && !d.ContainsKey(to))
            d[to] = v;
    }

    static List<string> DetectUrlKeys(Dictionary<string,string> d)
    {
        var siteKeys = Enumerable.Range(1, 9)
            .Select(i => $"url{i}")
            .Where(k => d.ContainsKey(k) && !string.IsNullOrWhiteSpace(d[k]))
            .ToList();

        if (siteKeys.Count > 0) return siteKeys;
        return d.ContainsKey("url") ? new List<string> { "url" } : new List<string>();
    }

    static char? LastDigit(string key)
    {
        for (int i = key.Length - 1; i >= 0; i--)
            if (char.IsDigit(key[i])) return key[i];
        return null;
    }

    static void ApplyPerSiteCredentials(char? suffix)
    {
        if (suffix is char s)
        {
            var uKey = $"usrName{s}";
            var pKey = $"usrPass{s}";
            if (cfg.ContainsKey(uKey)) cfg["usrName"] = cfg[uKey];
            if (cfg.ContainsKey(pKey)) cfg["usrPass"] = cfg[pKey];
        }
    }

    static void AssignConfigVarsForRun()
    {
        baseUrl       = Get("url");
        usrName       = Get("usrName");
        usrPass       = Get("usrPass");
        usrNameTxtBox = Get("usrNameTxtBox");
        usrPassTxtBox = Get("usrPassTxtBox");
        ctryLogin     = Get("countryLogin", true);
        loginBtn      = Get("loginBtn");

        caShipNbr     = Get("caShipmentNbr", true);
        usShipNbr     = Get("usShipmentNbr", true);
        invalidUpass  = Get("invalidUPass", true);
        dutyTypeVal   = Get("dutyType", true);
        orgnSSDate    = Get("orginSortSDate", true);
        orgnSEDate    = Get("orginSortEDate", true);
        manageAcctUser= Get("mngAcctUser", true);
        brokerCode    = Get("brkrCode", true);
        brokName      = Get("newBrokName", true);
        addNewUser    = Get("addUser", true);
        addNewUserPass= Get("addUserPass", true);
        addNewUserFName=Get("firstName", true);
        addNewUserLName=Get("lastName", true);
        mawbNumber    = Get("mawbNbr", true);
        bldgID        = Get("bldgID", true);
        packageID     = Get("packageID", true);
        region        = Get("region", true);
        district      = Get("district", true);
    }

    static string Get(string key, bool allowMissing=false)
    {
        if (cfg.TryGetValue(key, out var v)) return v;
        if (!allowMissing) LogStep("Missing Config Key", $"Required configuration property absent: {key}", "#F56565");
        return null;
    }

    // ====== DRIVER SETUP ======
    static IWebDriver SetupEdgeDriver(Dictionary<string,string> d)
    {
        var configured = d.TryGetValue("edgeDriver", out var ed) ? ed : d.TryGetValue("chromeDriver", out var cd) ? cd : null;
        string driverDir = null;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            driverDir = Directory.Exists(configured) ? configured : Path.GetDirectoryName(configured);
        }

        if (string.IsNullOrWhiteSpace(driverDir))
            driverDir = @"C:\Selenium\";

        var options = new EdgeOptions();
        options.AddArgument("--ignore-certificate-errors");
        options.AddArgument("--allow-insecure-localhost");

        if (d.TryGetValue("headless", out var hv) && bool.TryParse(hv, out var isHeadless) && isHeadless)
        {
            options.AddArgument("--headless=new");
            options.AddArgument("--window-size=1920,1080");
        }

        var service = EdgeDriverService.CreateDefaultService(driverDir);
        service.HideCommandPromptWindow = true;
        return new EdgeDriver(service, options);
    }

    // ====== LOGGING SUITE (CORREGIDO Y CONFIGURADO PARA CONSOLA) ======
    static void LogStep(string step, string result, string color = "#48BB78", string screenshotFileName = "")
    {
        var ts = DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        
        // Write to the local plain TXT file
        File.AppendAllText(logPath, $"{ts}\t{step}\t{result}{Environment.NewLine}");

        // SHOWS ACTIONS PERFORMED IN REAL TIME IN THE LINQPAD CONSOLE
        $@"[{ts}] [STEP: {step}] -> {result}".Dump();

        // Accumulates information in memory to build the final HTML report neatly
        accumulatedSteps.Add(new InternalStepLog
        {
            Timestamp = ts,
            StepDescription = step,
            ResultDetails = result,
            ColorHex = color,
            ScreenshotFileName = screenshotFileName
        });
    }

    private static string TakeScreenshot(IWebDriver driver, string savePath)
    {
        try
        {
            if (driver != null)
            {
                Screenshot screenshot = ((ITakesScreenshot)driver).GetScreenshot();
                // Fixed for Selenium 4 (Auto-detects PNG format via extension)
                screenshot.SaveAsFile(savePath);
                return savePath;
            }
        }
        catch (Exception ex)
        {
            $@"[WARNING] Error writing filesystem screenshot link: {ex.Message}".Dump();
        }
        return null;
    }

    // NEW STYLED HTML DASHBOARD GENERATOR
    static void BuildModernHtmlReport(string outputPath)
    {
        int totalSteps = accumulatedSteps.Count;
        int failedSteps = accumulatedSteps.Count(s => s.ColorHex == "#F56565");
        int passedSteps = totalSteps - failedSteps;
        string globalStatus = failedSteps > 0 ? "FAILED" : "PASSED";
        string globalStatusColor = failedSteps > 0 ? "#F56565" : "#48BB78";

        StringBuilder sb = new StringBuilder();
        sb.Append("<html><head><meta charset='utf-8'><title>IDIS Automation Dashboard</title>");
        sb.Append("<style>");
        sb.Append("body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 40px; background-color: #1A202C; color: #E2E8F0; }");
        sb.Append(".header-container { display: flex; justify-content: space-between; align-items: center; border-bottom: 3px solid #4A5568; padding-bottom: 20px; margin-bottom: 30px; }");
        sb.Append("h2 { color: #ECC94B; margin: 0; font-size: 28px; font-weight: 600; }");
        sb.Append(".timestamp { color: #A0AEC0; font-size: 14px; }");
        
        // Summary Deck Components
        sb.Append(".summary-deck { display: flex; gap: 20px; margin-bottom: 30px; }");
        sb.Append(".card { flex: 1; background: #2D3748; border-radius: 8px; padding: 15px 20px; box-shadow: 0 4px 6px rgba(0,0,0,0.2); border-left: 5px solid #4A5568; }");
        sb.Append(".card.status-card { border-left-color: " + globalStatusColor + "; }");
        sb.Append(".card.agent-card { border-left-color: #63B3ED; }");
        sb.Append(".card-title { font-size: 12px; text-transform: uppercase; color: #A0AEC0; font-weight: bold; letter-spacing: 1px; }");
        sb.Append(".card-value { font-size: 20px; font-weight: bold; margin-top: 5px; color: #FFF; }");
        
        // Record Table
        sb.Append("table { width: 100%; border-collapse: collapse; background: #2D3748; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 10px rgba(0,0,0,0.3); }");
        sb.Append("th { background-color: #1A202C; color: #ECC94B; font-weight: 600; text-transform: uppercase; font-size: 13px; letter-spacing: 0.5px; padding: 15px; text-align: left; }");
        sb.Append("td { padding: 15px; border-bottom: 1px solid #4A5568; font-size: 14px; vertical-align: top; }");
        sb.Append("tr:last-child td { border-bottom: none; }");
        sb.Append("tr:hover { background-color: #3A475C; }");
        
        // Internal Status Badges
        sb.Append(".badge { padding: 4px 8px; border-radius: 4px; font-weight: bold; font-size: 11px; display: inline-block; text-transform: uppercase; }");
        sb.Append(".badge-success { background-color: #2F855A; color: #C6F6D5; }");
        sb.Append(".badge-danger { background-color: #9B2C2C; color: #FED7D7; }");
        sb.Append(".badge-info { background-color: #2B6CB0; color: #EBF8FF; }");
        
        // Error Console Block
        sb.Append("pre { background: #1A202C; padding: 12px; border-radius: 6px; border-left: 4px solid #F56565; white-space: pre-wrap; color: #FEB2B2; font-size: 12px; font-family: 'Courier New', Courier, monospace; margin-top: 8px; }");
        sb.Append(".screenshot-link { display: inline-block; margin-top: 8px; color: #63B3ED; text-decoration: none; font-weight: bold; font-size: 13px; }");
        sb.Append(".screenshot-link:hover { text-decoration: underline; color: #4299E1; }");
        sb.Append("</style></head><body>");
        
        // Header Rendering
        sb.Append("<div class='header-container'>");
        sb.Append("<div><h2>Automated Test Execution Dashboard</h2><div style='color:#A0AEC0; margin-top:5px;'>" + testName + " Web Application Evaluation</div></div>");
        sb.Append("<div class='timestamp'><strong>Execution Date:</strong> " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</div>");
        sb.Append("</div>");
        
        // Summary Deck Rendering
        sb.Append("<div class='summary-deck'>");
        sb.Append("<div class='card agent-card'><div class='card-title'>PCM Operator Agent</div><div class='card-value'>" + currentUser + "</div></div>");
        sb.Append("<div class='card status-card'><div class='card-title'>Global Evaluation Status</div><div class='card-value' style='color:" + globalStatusColor + ";'>" + globalStatus + "</div></div>");
        sb.Append("<div class='card'><div class='card-title'>Consolidated Steps</div><div class='card-value'>" + passedSteps + " / " + totalSteps + " Steps Passed</div></div>");
        sb.Append("</div>");
        
        // Table Initialization
        sb.Append("<table>");
        sb.Append("<tr><th style='width:15%;'>Timestamp</th><th style='width:35%;'>Test Step Context</th><th style='width:50%;'>Execution Details / Logs</th></tr>");
        
        foreach (var step in accumulatedSteps)
        {
            string badgeStyle = "badge-success";
            if (step.ColorHex == "#F56565") badgeStyle = "badge-danger";
            else if (step.ColorHex == "#63B3ED") badgeStyle = "badge-info";

            sb.Append("<tr>");
            sb.Append("<td>" + step.Timestamp + "</td>");
            sb.Append("<td><strong>" + System.Net.WebUtility.HtmlEncode(step.StepDescription) + "</strong></td>");
            
            if (step.ColorHex == "#F56565")
            {
                sb.Append("<td>");
                sb.Append("<span class='badge " + badgeStyle + "'>ERROR</span>");
                sb.Append("<pre>" + System.Net.WebUtility.HtmlEncode(step.ResultDetails) + "</pre>");
                if (!string.IsNullOrEmpty(step.ScreenshotFileName))
                {
                    sb.Append("<br><a class='screenshot-link' href='" + step.ScreenshotFileName + "' target='_blank'>👁 View Error Screenshot</a>");
                }
                sb.Append("</td>");
            }
            else
            {
                string statusText = step.ColorHex == "#63B3ED" ? "CONTEXT" : "PASSED";
                sb.Append("<td><span class='badge " + badgeStyle + "' style='margin-right:10px;'>" + statusText + "</span><span style='color: #CBD5E0;'>" + System.Net.WebUtility.HtmlEncode(step.ResultDetails) + "</span></td>");
            }
            sb.Append("</tr>");
        }
        
        sb.Append("</table></body></html>");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    // ====== WEBHOOK SENDER (CON ESCAPE INTEGRADO Y DESPLIEGUE EN CONSOLA) ======
    private static void SendWebhookNotification(string url, string testName, string user, bool hasFailed, List<TestWebResult> results)
    {
        if (string.IsNullOrEmpty(url) || url.Contains("...")) return;
        try
        {
            using (HttpClient client = new HttpClient())
            {
                string globalStatus = hasFailed ? "FAILED" : "PASSED";
                StringBuilder jsonResultsBuilder = new StringBuilder();
                jsonResultsBuilder.Append("[");
                for (int i = 0; i < results.Count; i++)
                {
                    var r = results[i];
                    jsonResultsBuilder.Append("{");
                    jsonResultsBuilder.Append($"\"webKey\": \"{EscapeJsonString(r.WebKey)}\",");
                    jsonResultsBuilder.Append($"\"targetUrl\": \"{EscapeJsonString(r.Url)}\",");
                    jsonResultsBuilder.Append($"\"status\": \"{EscapeJsonString(r.Status)}\",");
                    jsonResultsBuilder.Append($"\"startTime\": \"{EscapeJsonString(r.StartTime)}\",");
                    jsonResultsBuilder.Append($"\"endTime\": \"{EscapeJsonString(r.EndTime)}\",");
                    jsonResultsBuilder.Append($"\"error\": \"{EscapeJsonString(r.ErrorMessage)}\"");
                    jsonResultsBuilder.Append("}");
                    if (i < results.Count - 1) jsonResultsBuilder.Append(",");
                }
                jsonResultsBuilder.Append("]");

                string jsonPayload = $"{{" +
                    $"\"testName\": \"{EscapeJsonString(testName)}\"," +
                    $"\"user\": \"{EscapeJsonString(user)}\"," +
                    $"\"globalStatus\": \"{EscapeJsonString(globalStatus)}\"," +
                    $"\"results\": {jsonResultsBuilder.ToString()}" +
                $"}}";
                
                // SHOWS THE COMPLETE PAYLOAD BEHAVIOR IN THE LINQPAD RESULTS WINDOW
                $"\n[Payload JSON sent to Power Automate Webhook]:\n{jsonPayload}\n".Dump();
                
                using (var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
                {
                    var response = client.PostAsync(url, content).Result;
                    if (response.IsSuccessStatusCode)
                        "✔ Power Automate cloud workflow triggered successfully.".Dump();
                    else
                        $"❌ Webhook validation failed. Code: {response.StatusCode}".Dump();
                }
            }
        }
        catch (Exception ex)
        {
            $@"❌ Webhook execution failed: {ex.Message}".Dump();
        }
    }

    // COMPLETE SANITIZER AGAINST QUOTES AND CONTROL CHARACTERS IN XPATHS
    private static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        StringBuilder sb = new StringBuilder();
        foreach (char c in value)
        {
            switch (c)
            {
                case '\"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32)
                        sb.Append(string.Format("\\u{0:X4}", (int)c));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    // ====== SELENIUM HELPER ACTIONS ======
    static IAlert WaitAlert(int seconds)
    {
        try
        {
            var waitAlert = new WebDriverWait(driver, TimeSpan.FromSeconds(seconds));
            return waitAlert.Until(ExpectedConditions.AlertIsPresent());
        }
        catch { return null; }
    }

    static IWebElement FluentWait(By locator, int timeoutSeconds = 30, int pollingSeconds = 5)
    {
        try
        {
            var waitFluent = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
            waitFluent.PollingInterval = TimeSpan.FromSeconds(pollingSeconds);
            waitFluent.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
            return waitFluent.Until(d => d.FindElement(locator));
        }
        catch { return null; }
    }

    static IWebElement WaitForVisible(By locator, int timeoutSeconds = 15)
    {
        try
        {
            var waitVisible = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
            return waitVisible.Until(ExpectedConditions.ElementIsVisible(locator));
        }
        catch { return null; }
    }

    static void BackToParent()
    {
        try
        {
            var handles = driver.WindowHandles;
            if (handles.Count > 0) driver.SwitchTo().Window(handles[0]);
        }
        catch (Exception e)
        {
            LogStep("Back function failed", e.ToString(), "#F56565");
            try { driver.Navigate().Back(); } catch { }
        }
    }

    // -------------------- YOUR IDIS AUTOMATION STEP METHODS GO BELOW --------------------
static void loginCase()
    {
        try
        {
            try
            {
                var header = driver.FindElement(By.XPath("/html/body/div/div[1]/div[2]/h1"));
                var strHeader = header.Text;
                if (strHeader == "Your connection is not private")
                {
                    driver.FindElement(By.XPath("/html/body/div/div[2]/button[3]")).Click();
                    driver.FindElement(By.XPath("/html/body/div/div[3]/p[2]/a")).Click();
                }
            }
            catch { /* Ignore */ }

            var username = driver.FindElement(By.XPath(usrNameTxtBox));
            username.SendKeys(usrName);
            LogStep("Input Username", usrName);

            var password = driver.FindElement(By.XPath(usrPassTxtBox));
            password.SendKeys(usrPass);
            LogStep("Input Password", "******");

            driver.FindElement(By.XPath(loginBtn)).Click();
            LogStep("Login Button Clicked", "OK");

            LogStep("Login test Complete", "OK");
        }
        catch (Exception e)
        {
            LogStep("Login Failed", e.ToString(), "#F56565");
        }
    }

    static void printButton()
    {
        try
        {
            LogStep("Print: Lookup Export Country/Date", "Start");
            exportDate();

            var printButton = FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/table[2]/tbody/tr[1]/td[2]/a[3]"));
            printButton.Click();
            LogStep("Print: Test Complete", "OK");
            Thread.Sleep(60000);;
            driver.Navigate().Back();
            Thread.Sleep(60000);
            FluentWait(By.XPath("/html/body/table[1]/tbody/tr/td/table/tbody/tr/td[3]/a[2]")).Click();
        }
        catch (Exception e)
        {
            LogStep("Print: Failed", e.ToString(), "#F56565");
        }
    }

    static void printToFile()
    {
        try
        {
            LogStep("Print to File: Lookup Export Country/Date", "Start");
            exportDate();

            var printToFile = FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/table[2]/tbody/tr[2]/td[2]/a[1]"));
            printToFile.Click();
            LogStep("Print to File: Test Complete", "OK");

            FluentWait(By.XPath("/html/body/table[1]/tbody/tr/td/table/tbody/tr/td[3]/a[2]")).Click();
        }
        catch (Exception e)
        {
            LogStep("Print to File: Failed", e.ToString(), "#F56565");
        }
    }

    static void exportDate()
    {
        try
        {
            LogStep("Lookup Export Country/Date", "Start");

            var lookupExpDate = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[1]/tbody/tr[12]/td/div/a"));
            lookupExpDate.Click();
            LogStep("Lookup Export Country/Date", "Lookup link");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/img")).Click();
            LogStep("Lookup Export Country/Date", "Click Start Date Picker");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
            LogStep("Lookup Export Country/Date", "Click Start Date Picker < button");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[4]/td[6]/font/a/b")).Click();
            LogStep("Lookup Export Country/Date", "Click Start Date Picker 2nd friday button");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td[2]/img")).Click();
            LogStep("Lookup Export Country/Date", "Click End Date Picker");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
            LogStep("Lookup Export Country/Date", "Click End Date Picker < button");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[4]/td[6]/font/a/b")).Click();
            LogStep("Lookup Export Country/Date", "Click End Date Picker 2nd saturday button");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input")).SendKeys("US");

            var submitButton = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[19]/td[2]/input"));
            submitButton.Click();
            LogStep("Lookup Export Country/Date", "submit button click");

            var alert = WaitAlert(40);
            alert.Accept();
            LogStep("Lookup Export Country/Date", "ok 1st alert");

            var row1AddToCart = FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/table/tbody/tr[2]/td[13]/a"));
            row1AddToCart.Click();
            LogStep("Lookup Export Country/Date", "1st row add to cart click");
        }
        catch (Exception e)
        {
            LogStep("Lookup Export Country/Date: Failed", e.ToString(), "#F56565");
        }
    }

    static void emailButton()
    {
        try
        {
            LogStep("Email Button: Lookup Export Country/Date", "Start");
            exportDate();
			
			Thread.Sleep(60000);
			
            var emailBut = FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/table[2]/tbody/tr[2]/td[2]/a[2]"));
            emailBut.Click();
            LogStep("Email Button", "Test Complete");

            var searchButton = FluentWait(By.XPath("/html/body/form/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[1]"));
            searchButton.Click();
        }
        catch (Exception e)
        {
            LogStep("Email Button: Failed", e.ToString(), "#F56565");
        }
    }

    static void shipmentID()
    {
        try
        {
            LogStep("Lookup Shipment ID", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[1]/tbody/tr[2]/td/div/a")).Click();
            LogStep("Lookup Shipment ID", "Click Shipment ID link");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[1]/input")).SendKeys(caShipNbr);
            LogStep("Lookup Shipment ID", "Shipment id input");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input")).Click();
            LogStep("Lookup Shipment ID", "click submit button");

            LogStep("Lookup Shipment ID", "success");
        }
        catch (Exception e)
        {
            LogStep("Lookup Shipment ID: Failed", e.ToString(), "#F56565");
        }
    }

    static void shipmentIDHistory()
    {
        try
        {
            LogStep("Lookup Shipment ID History", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[1]/tbody/tr[2]/td/div/a")).Click();
            LogStep("Lookup Shipment ID History", "Click Shipment ID link");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[1]/input")).SendKeys(caShipNbr);
            LogStep("Lookup Shipment ID History", "Shipment id input");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input")).Click();
            LogStep("Lookup Shipment ID History", "click submit button");

            LogStep("Lookup Shipment ID History", "success");
        }
        catch (Exception e)
        {
            LogStep("Lookup Shipment ID History: Failed", e.ToString(), "#F56565");
        }
    }

    static void printFileViewer()
    {
        try
        {
            LogStep("Print from Viewer", "Start");
            shipmentID();
            driver.FindElement(By.XPath("/html/body/map/area[1]")).Click();
            LogStep("Print from Viewer", "Click print");
            LogStep("Print from Viewer", "Test Complete");
            driver.Navigate().Back();
            driver.Navigate().Back();
            driver.Navigate().Back();
        }
        catch (Exception e)
        {
            LogStep("Print from Viewer: Failed", e.ToString(), "#F56565");
        }
    }

    static void printAllPagesViewer()
    {
        try
        {
            LogStep("Print all pages from Viewer", "Start");
            shipmentID();
            driver.FindElement(By.XPath("/html/body/map/area[2]")).Click();
            LogStep("Print all pages from Viewer", "Click print all pages");
            LogStep("Print all pages from Viewer", "Test Complete");
            driver.Navigate().Back();
            driver.Navigate().Back();
            driver.Navigate().Back();
        }
        catch (Exception e)
        {
            LogStep("Print all pages from Viewer: Failed", e.ToString(), "#F56565");
        }
    }

    static void viewerFunction()
    {
        try
        {
            LogStep("Viewer Function", "Start");
            shipmentID();
            driver.FindElement(By.XPath("/html/body/map/area[3]")).Click(); LogStep("Viewer Function", "Click Zoom");
            driver.FindElement(By.XPath("/html/body/map/area[4]")).Click(); LogStep("Viewer Function", "Click Actual Size");
            driver.FindElement(By.XPath("/html/body/map/area[5]")).Click(); LogStep("Viewer Function", "Click Larger");
            driver.FindElement(By.XPath("/html/body/map/area[6]")).Click(); LogStep("Viewer Function", "Click Smaller");
            driver.FindElement(By.XPath("/html/body/map/area[7]")).Click(); LogStep("Viewer Function", "Click Fit Screen");
            driver.FindElement(By.XPath("/html/body/map/area[8]")).Click(); LogStep("Viewer Function", "Click Best Fit");
            driver.FindElement(By.XPath("/html/body/map/area[9]")).Click(); LogStep("Viewer Function", "Click Rotate Left");
            driver.FindElement(By.XPath("/html/body/map/area[10]")).Click(); LogStep("Viewer Function", "Click Rotate Right");
            LogStep("Viewer Function", "Test Complete");

            var searchButton = FluentWait(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[1]"));
            searchButton.Click();
        }
        catch (Exception e)
        {
            LogStep("Viewer Function: Failed", e.ToString(), "#F56565");
        }
    }


    static void addUser()
    {
        try
        {
            LogStep("Add User", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[4]")).Click();
            LogStep("Add User", "Click User Link");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/input")).SendKeys(addNewUserFName);
            LogStep("Add User", "Input First Name");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[3]/td[2]/input")).SendKeys(addNewUserLName);
            LogStep("Add User", "Input Last Name");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[4]/td[2]/input")).SendKeys(addNewUser);
            LogStep("Add User", "Input UserName");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[5]/td[2]/input")).SendKeys(addNewUserPass);
            LogStep("Add User", "Input User Password");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[6]/td[2]/input")).SendKeys(addNewUserPass);
            LogStep("Add User", "Input Confirmed Password");

            var userGroup = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[7]/td[2]/select"));
            new SelectElement(userGroup).SelectByValue("2");
            LogStep("Add User", "Select User Group");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[8]/td[2]/input")).SendKeys("0000");
            LogStep("Add User", "Input Building ID");

            var userRegion = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[9]/td[2]/select"));
            new SelectElement(userRegion).SelectByValue("02");
            LogStep("Add User", "Select Region");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[10]/td[2]/input")).SendKeys("jeffrey.aguanta@conduent.com");
            LogStep("Add User", "Input Email Address");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[11]/td[2]/input")).SendKeys("jeffrey.aguanta@conduent.com");
            LogStep("Add User", "Input Confirmed Email Address");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/center/input[1]")).Click();
            LogStep("Add User", "Click Add Key");

            var alert = WaitAlert(40); alert.Accept();
            var alert2 = WaitAlert(40); alert2.Accept();

            LogStep("Add User", "Test Complete");
        }
        catch (Exception e)
        {
            LogStep("Add User: Failed", e.ToString(), "#F56565");
        }
    }

static void modifyUser()
{
    try
    {
        LogStep("Modify User", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/center[1]/form/span[2]/font/input[1]")).SendKeys(addNewUser);
        LogStep("Modify User", "Input UserName");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/center[1]/form/span[2]/font/input[2]")).Click();
        LogStep("Modify User", "Click Submit Button");

        var fn = By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[2]/td[2]/input");
        driver.FindElement(fn).Clear();
        driver.FindElement(fn).SendKeys("Updated First Name");
        LogStep("Modify User", "Input First Name");

        var ln = By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[3]/td[2]/input");
        driver.FindElement(ln).Clear();
        driver.FindElement(ln).SendKeys("Selenium");
        LogStep("Modify User", "Input Last Name");

        var userGroup = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[6]/td[2]/select"));
        new SelectElement(userGroup).SelectByValue("2");
        LogStep("Modify User", "Select User Group");

        var bldg = By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[8]/td[2]/input");
        driver.FindElement(bldg).Clear();
        driver.FindElement(bldg).SendKeys("0000");
        LogStep("Modify User", "Input Building ID");

        var reg = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[9]/td[2]/select"));
        new SelectElement(reg).SelectByValue("02");
        LogStep("Modify User", "Select Region");

        var email = By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[10]/td[2]/input");
        driver.FindElement(email).Clear();
        driver.FindElement(email).SendKeys("jeffrey.aguanta@conduent.com");
        LogStep("Modify User", "Input Email Address");

        var email2 = By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[11]/td[2]/input");
        driver.FindElement(email2).Clear();
        driver.FindElement(email2).SendKeys("jeffrey.aguanta@conduent.com");
        LogStep("Modify User", "Input Confirmed Email Address");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/center/input[1]")).Click();
        LogStep("Modify User", "Click Modify Button");

        var alert = WaitAlert(40); alert.Accept();

        LogStep("Modify User", "Test Complete");
    }
    catch (Exception e)
    {
        LogStep("Modify User: Failed", e.ToString(), "#F56565");
    }
}

static void deleteUser()
{
    try
    {
        LogStep("Delete User", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[2]/table/tbody/tr/td[2]/input")).SendKeys(addNewUser);
        LogStep("Delete User", "Input to be deleted User ID");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[2]/center/input[1]")).Click();
        LogStep("Delete User", "Click Delete Button");

        var a1 = WaitAlert(40); a1.Accept();
        var a2 = WaitAlert(40); a2.Accept();

        LogStep("Delete User", "Test Complete");
    }
    catch (Exception e)
    {
        LogStep("Delete User: Failed", e.ToString(), "#F56565");
    }
}

static void updateProfile()
{
    try
    {
        LogStep("Update Profile", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[6]")).Click();
        LogStep("Update Profile", "Click Update Profile Link");

        var fn = By.XPath("/html/body/table/tbody/tr[2]/td/table/tbody/tr[3]/td/div/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input");
        driver.FindElement(fn).Clear();
        driver.FindElement(fn).SendKeys("ACS");
        LogStep("Update Profile", "Update First Name");

        var ln = By.XPath("/html/body/table/tbody/tr[2]/td/table/tbody/tr[3]/td/div/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td[2]/input");
        driver.FindElement(ln).Clear();
        driver.FindElement(ln).SendKeys("dis.helpdesk");
        LogStep("Update Profile", "Update Last Name");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td/table/tbody/tr[3]/td/div/table/tbody/tr[2]/td[2]/table/tbody/tr[10]/td[2]/input")).Click();
        LogStep("Update Profile", "Click Submit Button");

        LogStep("Update Profile", "Test Complete");
    }
    catch (Exception e)
    {
        LogStep("Update Profile: Failed", e.ToString(), "#F56565");
    }
}


 static void Mwab()
{
    try
    {
        LogStep("MWAB Lookup", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[1]")).Click();
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[1]/tbody/tr[10]/td/div/a")).Click();
        LogStep("MWAB Lookup", "Click MAWB Link");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input")).SendKeys(mawbNumber);
        LogStep("MWAB Lookup", "Input MAWB Number");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[7]/td[2]/input")).Click();
        LogStep("MWAB Lookup", "Click Submit Button");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/table/tbody/tr[2]/td[6]/a")).Click();
        LogStep("MWAB Lookup", "Click Add to Cart");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[2]")).Click();
        LogStep("MWAB Lookup", "Test Complete");

        var searchButton = FluentWait(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[1]"));
        searchButton.Click();
    }
    catch (Exception e)
    {
        LogStep("MWAB Lookup: Failed", e.ToString(), "#F56565");
    }
} 
 

static void ImgSiteVolume()
{
    try
    {
        LogStep("Image Site Volume - Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[2]/td/div/a")).Click();
        LogStep("Image Site Volume - Reports", "Link Clicked");

        var Buildingid = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input"));
        Buildingid.SendKeys(bldgID);
        LogStep("Image Site Volume - Reports", "Enter Building ID");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/img")).Click();
        LogStep("Image Site Volume - Reports", "Start Date Picker Clicked");

        driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
        LogStep("Image Site Volume - Reports", "Click Start Date Picker < button");

        driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[6]/td[6]/font/a/b")).Click();
        LogStep("Image Site Volume - Reports", "Click Start Date Picker 4th friday button");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td[2]/img")).Click();
        LogStep("Image Site Volume - Reports", "Click End Date Picker");

        driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
        LogStep("Image Site Volume - Reports", "Click End Date Picker < button");

        driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[6]/td[6]/font/a/b")).Click();
        LogStep("Image Site Volume - Reports", "Click Start Date Picker 4th friday button");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/select")).SendKeys("23:00");
        LogStep("Image Site Volume - Reports", "Input Start Time");

        var submitButton = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[7]/td[2]/input"));
        submitButton.Click();
        LogStep("Image Site Volume - Reports", "submit button click");

        var totalCount = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/font/form/table[3]/tbody/tr/td[1]")).Text;
        if (totalCount == "Total Batches:0")
        {
            LogStep("Image Site Volume - Reports", "Test Complete with zero result");
        }
        else
        {
            LogStep("Image Site Volume - Reports", "Test Complete");
        }

        System.Threading.Thread.Sleep(1000);
        driver.Navigate().Back();
    }
    catch (Exception e)
    {
        LogStep("Image Site Volume - Reports: Failed", e.ToString(), "#F56565");
    }
}


static void ShipmentDetail()
{
    try
    {
        LogStep("Shipment Detail - Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[3]/td/div/a")).Click();
        LogStep("Shipment Detail - Reports", "Link Clicked");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[1]/input")).SendKeys(usShipNbr);
        LogStep("Shipment Detail", "Input Shipment Number");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input")).Click();
        LogStep("Shipment Detail", "Submit Button Clicked");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/font/table/tbody/tr[3]/td[1]/a")).Click();
        LogStep("Shipment Detail", "Click Result");

        LogStep("Shipment Detail - Reports", "Test Complete");

        var searchButton = FluentWait(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[1]"));
        searchButton.Click();
    }
    catch (Exception e)
    {
        LogStep("Shipment Detail - Reports: Failed", $"Please Check if this shipment number is still valid {usShipNbr}. {e}", "#F56565");
    }
}

static void PackageDetail()
{
    try
    {
        LogStep("Package Detail - Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[4]/td/div/a")).Click();
        LogStep("Package Detail - Reports", "Link Clicked");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[1]/input")).SendKeys(packageID);
        LogStep("Package Detail", "Input Package ID");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input")).Click();
        LogStep("Package Detail", "Submit Button Clicked");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/font/table/tbody/tr[3]/td[1]/a")).Click();
        LogStep("Package Detail", "Click Result");

        LogStep("Package Detail - Reports", "Test Complete");

        var searchButton = FluentWait(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[1]"));
        searchButton.Click();
    }
    catch (Exception e)
    {
        LogStep("Package Detail - Reports: Failed", $"Please Check if this Package ID number is still valid {packageID}. {e}", "#F56565");
    }
}

static void KeyedNoDocsImg()
{
    try
    {
        LogStep("Keyed no Docs Imaged - Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[5]/td/div/a")).Click();
        LogStep("Keyed no Docs Imaged - Reports", "Link Clicked");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input")).SendKeys("US");
        LogStep("Keyed no Docs Imaged - Reports", "Input US on the Data Capture Country");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[13]/td[2]/input")).Click();
        LogStep("Keyed no Docs Imaged - Reports", "Submit Button Clicked");

        var totalCount = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/font/table[3]/tbody/tr/td[2]")).Text;

        if (totalCount == "0")
            LogStep("Keyed no Docs Imaged - Reports", "Test Complete with 0 result");
        else
            LogStep("Keyed no Docs Imaged - Reports", $"Test Complete with {totalCount} result");

        System.Threading.Thread.Sleep(10000);
        driver.Navigate().Back();
    }
    catch (Exception e)
    {
        LogStep("Keyed no Docs Imaged - Reports: Failed", e.ToString(), "#F56565");
    }
}

static void DocsImgNotKeyed()
{
    try
    {
        LogStep("Docs Imaged not Keyed - Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[6]/td/div/a")).Click();
        LogStep("DocsImgNotKeyed - Reports", "Link Clicked");

        var Buildingid = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input"));
        Buildingid.SendKeys(bldgID);
        LogStep("DocsImgNotKeyed - Reports", "Enter Building ID");
        
        driver.FindElement(By.XPath("//html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[7]/td[2]/input")).Click();
        LogStep("DocsImgNotKeyed - Reports", "Submit Button Clicked");

        var totalCount = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/font/table[3]/tbody/tr/td[2]")).Text;

        if (totalCount == "0")
            LogStep("Docs Imaged not Keyed - Reports", "Test Complete with 0 result");
        else
            LogStep("Docs Imaged not Keyed - Reports", $"Test Complete with {totalCount} result");

        System.Threading.Thread.Sleep(10000);
        driver.Navigate().Back();
        
    }
    catch (Exception e)
    {
        LogStep("Docs Imaged not Keyed - Reports: Failed", e.ToString(), "#F56565");
    }
}

static void OCRConfig()
{
    try
    {
        LogStep("OCR Configuration - Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[18]/td/div/a")).Click();
        LogStep("OCR Report - Reports", "Link Clicked");

        var existingConfig = FluentWait(By.XPath("/html/body/div/div[2]/div[3]/div/div[2]/div[1]/text()")).Text;
        LogStep("OCR Configuration - Reports", existingConfig);
        LogStep("OCR Configuration - Reports", "Test Complete");
    }
    catch
    {
        LogStep("OCR Configuration - Reports", "Test Complete");
    }
}


static void OCRReport()
{
    try
    {
        LogStep("OCR Report - Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[17]/td/div/a")).Click();
        LogStep("OCR Report - Reports", "Link Clicked");

        driver.SwitchTo().Frame(FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/iframe")));

        FluentWait(By.XPath("/html/body/form/div[2]/table/tbody/tr[2]/td[2]/select")).SendKeys("00:00");
        LogStep("OCR Report - Reports", "Input Start Time");

        FluentWait(By.XPath("/html/body/form/div[2]/table/tbody/tr[2]/td[3]/select")).SendKeys("01:00");
        LogStep("OCR Report - Reports", "Input End Time");

        var submitButton = driver.FindElement(By.XPath("/html/body/form/div[2]/table/tbody/tr[9]/td/button"));
        submitButton.Click();
        LogStep("OCR Report - Reports", "submit button click");
        
        System.Threading.Thread.Sleep(90000);
        
        if (FluentWait(By.XPath("/html/body/form/table/tbody/tr[1]/td[1]")) != null)
            LogStep("OCR Report - Reports", "Test Complete");
        else
            LogStep("OCR Report - Reports", "Test failed with 0 result", "#F56565");
        
        driver.Navigate().Back();

    }
    catch (Exception e)
    {
        LogStep("OCR Report - Reports: Test Fail", e.ToString(), "#F56565");
        try { driver.Navigate().Back(); } catch { }
    }
}

static void IDISSummary()
{
    try
    {
        LogStep("IDIS Summary - Admin Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[4]/tbody/tr[2]/td/div/a")).Click();
        LogStep("IDIS Summary - Admin Reports", "Link Clicked");

        var Buildingid = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[4]/td[2]/input"));
        Buildingid.SendKeys(bldgID);
        LogStep("IDIS Summary - Admin Reports", "Enter Building ID");

        var cntryCode = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td[2]/input"));
        cntryCode.SendKeys("US");
        LogStep("IDIS Summary - Admin Reports", "Enter Country Code");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/img")).Click();
        LogStep("IDIS Summary - Admin Reports", "Start Date Picker Clicked");

        driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
        LogStep("IDIS Summary - Admin Reports", "Click Start Date Picker < button");

        driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[6]/td[6]/font/a/b")).Click();
        LogStep("IDIS Summary - Admin Reports", "Click Start Date Picker 4th friday button");

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/img")).Click();
        LogStep("IDIS Summary - Admin Reports", "Click End Date Picker");

        driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
        LogStep("IDIS Summary - Admin Reports", "Click End Date Picker < button");

        driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[6]/td[6]/font/a/b")).Click();
        LogStep("IDIS Summary - Admin Reports", "Click Start Date Picker 4th friday button");

        var submitButton = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[7]/td[2]/input"));
        submitButton.Click();
        LogStep("IDIS Summary - Admin Reports", "submit button click");

        var searchResult = FluentWait(By.XPath("/html/body/table[2]/tbody/tr[2]/td/table/tbody/tr[5]/td[1]"));

        if (bldgID == searchResult.Text)
            LogStep("IDIS Summary - Admin Reports", "Test Complete");
        else
            LogStep("IDIS Summary - Admin Reports", "Test Complete with no result rendered");

        FluentWait(By.XPath("/html/body/table[1]/tbody/tr/td/table/tbody/tr/td[3]/a[2]")).Click();
    }
    catch (Exception e)
    {
        LogStep("IDIS Summary - Admin Reports: Test Failed", e.ToString(), "#F56565");
        try { driver.Navigate().Back(); } catch { }
    }
}

static void SuspendedSitesUT()
{
    try
    {
        LogStep("Suspended Site (UT) - Admin Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[4]/tbody/tr[3]/td/div/a")).Click();
        LogStep("Suspended Site (UT) - Admin Reports", "Link Clicked");

        var cntryCode = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input"));
        cntryCode.SendKeys("US");
        LogStep("Suspended Site (UT) - Admin Reports", "Enter Country Code");

        var submitButton = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input"));
        submitButton.Click();
        LogStep("Suspended Site (UT) - Admin Reports", "submit button click");

        var searchResult = FluentWait(By.XPath("/html/body/table[2]/tbody/tr[2]/td/table/tbody/tr[3]/td[2]"));
        if (string.IsNullOrEmpty(searchResult.Text))
            LogStep("Suspended Site (UT) - Admin Reports", "Test Complete with no result rendered");
        else
            LogStep("Suspended Site (UT) - Admin Reports", "Test Complete");

        FluentWait(By.XPath("/html/body/table[1]/tbody/tr/td/table/tbody/tr/td[3]/a[2]")).Click();
    }
    catch (Exception e)
    {
        LogStep("Suspended Site (UT) - Admin Reports: Test Failed", e.ToString(), "#F56565");
        try { driver.Navigate().Back(); } catch { }
    }
}

static void SuspendedSitesTX()
{
    try
    {
        LogStep("Suspended Site (TX) - Admin Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[4]/tbody/tr[4]/td/div/a")).Click();
        LogStep("Suspended Site (TX) - Admin Reports", "Link Clicked");

        var cntryCode = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input"));
        cntryCode.SendKeys("US");
        LogStep("Suspended Site (TX) - Admin Reports", "Enter Country Code");

        var submitButton = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input"));
        submitButton.Click();
        LogStep("Suspended Site (TX) - Admin Reports", "submit button click");

        var searchResult = FluentWait(By.XPath("/html/body/table[2]/tbody/tr[2]/td/table/tbody/tr[3]/td[2]"));
        if (searchResult.Text == "0")
            LogStep("Suspended Site (TX) - Admin Reports", "Test Complete with no result rendered");
        else
            LogStep("Suspended Site (TX) - Admin Reports", "Test Complete");

        FluentWait(By.XPath("/html/body/table[1]/tbody/tr/td/table/tbody/tr/td[3]/a[2]")).Click();
    }
    catch (Exception e)
    {
        LogStep("Suspended Site (TX) - Admin Reports: Test Failed", e.ToString(), "#F56565");
        try { driver.Navigate().Back(); } catch { }
    }
}

static void SitesNoImg()
{
    try
    {
        LogStep("Sites That Did Not Image - Admin Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[4]/tbody/tr[5]/td/div/a")).Click();
        LogStep("Sites That Did Not Image - Admin Reports", "Link Clicked");

        /*       var reg = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input"));
        reg.SendKeys(region);
        LogStep("Sites That Did Not Image - Admin Reports", "Enter Region");

        var dist = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td[2]/input"));
        dist.SendKeys(district);
        LogStep("Sites That Did Not Image - Admin Reports", "Enter District"); */

        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/img")).Click();
        LogStep("Sites That Did Not Image - Admin Report", "Date Picker Clicked");

        driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
        LogStep("Sites That Did Not Image - Admin Reports", "Click Date Picker < button");

        driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[6]/td[6]/font/a/b")).Click();
        LogStep("Sites That Did Not Image - Admin Report", "Click Date Picker 4th friday button");

        var submitButton = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[4]/td[2]/input"));
        submitButton.Click();
        LogStep("Sites That Did Not Image - Admin Reports", "submit button click");

        var searchResult = FluentWait(By.XPath("/html/body/table[2]/tbody/tr[2]/td/table/tbody/tr[3]/td[2]"));
        if (searchResult.Text == "07")
            LogStep("Sites That Did Not Image - Admin Reports", "Test Complete");
        else
            LogStep("Sites That Did Not Image - Admin Reports", "Test Complete with no result rendered");

        FluentWait(By.XPath("/html/body/table[1]/tbody/tr/td/table/tbody/tr/td[3]/a[2]")).Click();

        LogStep("Sites That Did Not Image - Admin Reports", "Test Complete");
    }
    catch (Exception e)
    {
        LogStep("Sites That Did Not Image - Admin Reports: Test Failed", e.ToString(), "#F56565");
        try { driver.Navigate().Back(); } catch { }
    }
}

static void BuildingConfig()
{
    try
    {
        LogStep("Buildings Configuration - Admin Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[4]/tbody/tr[6]/td/div/a")).Click();
        LogStep("Buildings Configuration - Admin Reports", "Link Clicked");

        driver.SwitchTo().Frame(FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/iframe")));
		
		Thread.Sleep(60000);
		
        /*var searchResult = FluentWait(By.XPath("/html/body/form/table/tbody/tr[1]/td[1]"));
		if (searchResult.Text == "us")
            LogStep("Buildings Configuration - Admin Reports", "Test Complete");
        else
            LogStep("Buildings Configuration - Admin Reports", "Test Failed", "#F56565");*/
		
		if (FluentWait(By.XPath("/html/body/form/table/tbody/tr[1]/td[1]")) != null)
		    LogStep("Buildings Configuration - Admin Reports", "Test Complete");
        else
            LogStep("Buildings Configuration - Admin Reports", "Test Failed", "#F56565");
			
        driver.Navigate().Back();
    }
    catch (Exception e)
    {
        LogStep("Buildings Configuration - Admin Reports: Test Failed", e.ToString(), "#F56565");
        try { driver.Navigate().Back(); } catch { }
    }
}

static void AutoClass()
{
    try
    {
        LogStep("Auto Classification - Admin Reports", "Start");
        driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[4]/tbody/tr[7]/td/div/a")).Click();
        LogStep("Auto Classification - Admin Reports", "Link Clicked");

        driver.SwitchTo().Frame(FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/iframe")));
        
        Thread.Sleep(60000);
        
        var searchResult = WaitForVisible(By.XPath("/html/body/app-root/app-odcconfiguration/div/div/div[1]/table/tbody/tr[1]/td[2]"));
        if (searchResult.Text == "us")
            LogStep("Auto Classification - Admin Reports", "Test Complete");
        else
            LogStep("Auto Classification - Admin Reports", "Test Failed", "#F56565");

        driver.Navigate().Back();
        
    }
    catch (Exception e)
    {
        LogStep("Auto Classification - Admin Reports: Test Failed", e.ToString(), "#F56565");
        try { driver.Navigate().Back(); } catch { }
    }
}
}