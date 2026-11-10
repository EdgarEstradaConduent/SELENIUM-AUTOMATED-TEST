<Query Kind="Program">
  <Reference Relative="..\msedgedriver.exe">C:\Selenium\msedgedriver.exe</Reference>
  <Reference Relative="..\SeleniumExtras.WaitHelpers.dll">C:\Selenium\SeleniumExtras.WaitHelpers.dll</Reference>
  <Reference Relative="..\WebDriver.dll">C:\Selenium\WebDriver.dll</Reference>
  <Reference Relative="..\WebDriver.Support.dll">C:\Selenium\WebDriver.Support.dll</Reference>
  <Namespace>OpenQA.Selenium</Namespace>
  <Namespace>OpenQA.Selenium.Edge</Namespace>
  <Namespace>OpenQA.Selenium.Support.UI</Namespace>
  <IncludeUncapsulator>false</IncludeUncapsulator>
</Query>

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Net.Http;

// 1. HELPER CLASS FOR INDIVIDUAL WEB RESULTS (Sent to the Webhook)
public class TestWebResult
{
    public string WebKey { get; set; }
    public string Url { get; set; }
    public string Status { get; set; } // PASSED or FAILED
    public string StartTime { get; set; }
    public string EndTime { get; set; }
    public string ErrorMessage { get; set; } = "";
}

// 2. HELPER CLASS FOR LOCAL LOGS AND REPORTS
public class TestStep
{
    public string Name { get; set; }
    public string Status { get; set; } // PASSED or FAILED
    public string Details { get; set; }
    public string ScreenshotPath { get; set; } 
    public string ExecutedBy { get; set; }
}

// 3. MAIN EXECUTION METHOD
void Main()
{
    // DYNAMIC TEST NAME CONFIGURATION
    string testName = "ICA"; 
    string siteLabel = "File Server (F5)"; // Friendly name of the only environment under test 
    string currentUser = Environment.UserName;
    
    string webhookUrl = "https://1d4fad50e8f7e1b0b1bebdb55a10c1.11.environment.api.powerplatform.com:443/powerautomate/automations/direct/cu/05/workflows/e46a99122aee4893a5a93fbbd4185970/triggers/manual/paths/invoke?api-version=1&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=Z-yibklShPefNQ1HXQ8J6IH_2eOAZe56Np8OLH4WwEE";

    var configPath = @"C:\Selenium\ICAWEB(BOC)-SANDY\config.properties";
    if (!File.Exists(configPath)) throw new FileNotFoundException($"Configuration file missing at: {configPath}");
    
    var config = LoadConfig(configPath);
    var urlKeys = new[] { "url1","url2","url3","url4","url5" }; // File Server (F5)

    string outputDir = @"C:\Selenium\ICAWEB(BOC)-SANDY\log\";
    string screenshotDir = Path.Combine(outputDir, "Screenshots");
    
    if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
    if (!Directory.Exists(screenshotDir)) Directory.CreateDirectory(screenshotDir);

    string htmlReportPath = Path.Combine(outputDir, "ICAWebApplication.html");
    string txtLogPath = Path.Combine(outputDir, "ICAWebApplication.txt");

    List<TestStep> allTestSteps = new List<TestStep>();
    List<TestWebResult> webResults = new List<TestWebResult>();
    bool globalFailure = false;

    $"=== STARTING {testName} AUTOMATED TESTS ===".Dump();

    foreach (var urlKey in urlKeys)
    {
        if (!config.ContainsKey(urlKey)) continue;
        
        var actualUrl = config[urlKey];
        $"--- Processing URL: {actualUrl} ---".Dump();

        var currentResult = new TestWebResult
        {
            WebKey = siteLabel,
            Url = actualUrl,
            StartTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Status = "PASSED"
        };
        
        bool currentUrlFailed = false;
        
        config["url"] = actualUrl;
        config["usrName"] = config[$"usrName{urlKey.Last()}"];
        config["usrPass"] = config[$"usrPass{urlKey.Last()}"];

        EdgeDriver driver = null;

        try
        {
            driver = SetupEdgeDriver();
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(15);
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);

            // Inline helper function to isolate errors per action
            void ExecuteStep(string stepName, Action action)
            {
                if (currentUrlFailed) return;

                try
                {
                    action();
                    allTestSteps.Add(new TestStep { Name = $"[{siteLabel}] {stepName}", Status = "PASSED", Details = "Step completed successfully.", ExecutedBy = currentUser });
                    $"[PASSED] {stepName}".Dump();
                }
                catch (Exception ex)
                {
                    currentUrlFailed = true;
                    globalFailure = true; 
                    currentResult.Status = "FAILED";
                    currentResult.ErrorMessage = ex.Message;
                    
                    string screenshotFile = $"{urlKey}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    string fullScreenshotPath = Path.Combine(screenshotDir, screenshotFile);
                    TakeScreenshot(driver, fullScreenshotPath);

                    allTestSteps.Add(new TestStep 
                    { 
                        Name = $"[{siteLabel}] {stepName}", 
                        Status = "FAILED", 
                        Details = ex.Message, 
                        ScreenshotPath = Path.Combine("Screenshots", screenshotFile), 
                        ExecutedBy = currentUser 
                    });
                    
                    $"[FAILED] {stepName} - Error: {ex.Message}".Dump();
                    File.AppendAllText(txtLogPath, $"{DateTime.Now:MM/dd/yyyy HH:mm:ss}\t{stepName}\tFailed: {ex.Message}{Environment.NewLine}");
                }
            }

            // --- SELENIUM TEST STEP FLOW ---
            ExecuteStep("Navigate to URL Target", () => driver.Navigate().GoToUrl(config["url"]));
            ExecuteStep("User Authentication Login", () => Login(driver, config));
            ExecuteStep("Process Low Value Case Report", () => ReportCase(driver, config));
            ExecuteStep("Verify Administration Console Access", () => UserAdmin(driver, config));
            ExecuteStep("Verify Account Management Access", () => ManageAccount(driver, config));
            ExecuteStep("Verify Firm Information Update Access", () => UpdateFirmInfo(driver, config));
            ExecuteStep("Process High Value Shipment Report", () => ReportHighValueShipment(driver, config));
        }
        catch (Exception ex)
        {
            currentUrlFailed = true;
            globalFailure = true;
            currentResult.Status = "FAILED";
            currentResult.ErrorMessage = ex.Message;
            $"Critical error initializing web driver session for {actualUrl}: {ex.Message}".Dump();
        }
        finally
        {
            if (driver != null)
            {
                driver.Quit();
            }
            
            currentResult.EndTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            webResults.Add(currentResult);
        }
    }

    // GENERATE CLEAN UNIFIED VISUAL HTML REPORT LOCAL
    GenerateHtmlReport(htmlReportPath, allTestSteps);
    
    // SEND DYNAMIC NOTIFICATION WITH THE FULL ARRAY AND WITHOUT SCREENSHOTS
    SendWebhookNotification(webhookUrl, testName, currentUser, globalFailure, webResults);
    
    "=== AUTOMATION PROCESS COMPLETED ===".Dump();
}

// MANAGEMENT CONFIGURATION AND DRIVER HELPERS
Dictionary<string, string> LoadConfig(string path)
{
    var dict = new Dictionary<string, string>();
    foreach (var line in File.ReadAllLines(path))
    {
        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2)
                dict[parts[0].Trim()] = parts[1].Trim();
        }
    }
    return dict;
}

EdgeDriver SetupEdgeDriver()
{
    var options = new EdgeOptions();
    options.AddArgument("--ignore-certificate-errors");
    options.AddArgument("--allow-insecure-localhost");
    
    var service = EdgeDriverService.CreateDefaultService(@"C:\Selenium\");
    service.HideCommandPromptWindow = true; 
    
    return new EdgeDriver(service, options);
}

IWebElement WaitForElement(EdgeDriver driver, By by, int seconds = 20)
{
    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(seconds));
    return wait.Until(d => d.FindElement(by));
}

private void TakeScreenshot(EdgeDriver driver, string savePath)
{
    try
    {
        if (driver != null)
        {
            Screenshot screenshot = ((ITakesScreenshot)driver).GetScreenshot();
            // Selenium 4 automatically detects the PNG format from the file extension
            screenshot.SaveAsFile(savePath); 
            $"[SCREENSHOT] Saved successfully at: {savePath}".Dump();
        }
    }
    catch (Exception ex)
    {
        $"[WARNING] Could not capture screenshot: {ex.Message}".Dump();
    }
}

// DYNAMIC WEBHOOK SENDING (FIXED TO PREVENT BLOCKING POWER AUTOMATE)
private void SendWebhookNotification(string url, string testName, string user, bool hasFailed, List<TestWebResult> results)
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

			$"\n[Payload JSON sent to Webhook]:\n{jsonPayload}\n".Dump();
			
            HttpContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = client.PostAsync(url, content).Result;
        }
    }
    catch (Exception ex)
    {
        $"❌ Webhook communication exception: {ex.Message}".Dump();
    }
}

// ROBUST ESCAPE HELPER FUNCTION
private string EscapeJsonString(string value)
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

private void GenerateHtmlReport(string filePath, List<TestStep> steps)
{
    int totalSteps = steps.Count;
    int failedSteps = steps.Count(s => s.Status == "FAILED");
    int passedSteps = totalSteps - failedSteps;
    string globalStatus = failedSteps > 0 ? "FAILED" : "PASSED";
    string globalStatusColor = failedSteps > 0 ? "#F56565" : "#48BB78";
    string operatorUser = steps.Count > 0 ? steps[0].ExecutedBy : "Unknown";

    StringBuilder sb = new StringBuilder();
    sb.Append("<html><head><meta charset='utf-8'><title>ICA Automation Dashboard</title>");
    sb.Append("<style>");
    sb.Append("body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 40px; background-color: #1A202C; color: #E2E8F0; }");
    sb.Append(".header-container { display: flex; justify-content: space-between; align-items: center; border-bottom: 3px solid #4A5568; padding-bottom: 20px; margin-bottom: 30px; }");
    sb.Append("h2 { color: #ECC94B; margin: 0; font-size: 28px; font-weight: 600; }");
    sb.Append(".timestamp { color: #A0AEC0; font-size: 14px; }");
    sb.Append(".summary-deck { display: flex; gap: 20px; margin-bottom: 30px; }");
    sb.Append(".card { flex: 1; background: #2D3748; border-radius: 8px; padding: 15px 20px; box-shadow: 0 4px 6px rgba(0,0,0,0.2); border-left: 5px solid #4A5568; }");
    sb.Append(".card.status-card { border-left-color: " + globalStatusColor + "; }");
    sb.Append(".card.agent-card { border-left-color: #63B3ED; }");
    sb.Append(".card-title { font-size: 12px; text-transform: uppercase; color: #A0AEC0; font-weight: bold; letter-spacing: 1px; }");
    sb.Append(".card-value { font-size: 20px; font-weight: bold; margin-top: 5px; color: #FFF; }");
    sb.Append("table { width: 100%; border-collapse: collapse; background: #2D3748; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 10px rgba(0,0,0,0.3); }");
    sb.Append("th { background-color: #1A202C; color: #ECC94B; font-weight: 600; text-transform: uppercase; font-size: 13px; letter-spacing: 0.5px; padding: 15px; text-align: left; }");
    sb.Append("td { padding: 15px; border-bottom: 1px solid #4A5568; font-size: 14px; vertical-align: top; }");
    sb.Append("tr:last-child td { border-bottom: none; }");
    sb.Append("tr:hover { background-color: #3A475C; }");
    sb.Append(".badge { padding: 4px 8px; border-radius: 4px; font-weight: bold; font-size: 11px; display: inline-block; text-transform: uppercase; }");
    sb.Append(".badge-success { background-color: #2F855A; color: #C6F6D5; }");
    sb.Append(".badge-danger { background-color: #9B2C2C; color: #FED7D7; }");
    sb.Append("pre { background: #1A202C; padding: 12px; border-radius: 6px; border-left: 4px solid #F56565; white-space: pre-wrap; color: #FEB2B2; font-size: 12px; font-family: 'Courier New', Courier, monospace; margin-top: 8px; }");
    sb.Append(".screenshot-link { display: inline-block; margin-top: 8px; color: #63B3ED; text-decoration: none; font-weight: bold; font-size: 13px; }");
    sb.Append(".screenshot-link:hover { text-decoration: underline; color: #4299E1; }");
    sb.Append("</style></head><body>");

    sb.Append("<div class='header-container'>");
    sb.Append("<div><h2>Automated Test Execution Dashboard</h2><div style='color:#A0AEC0; margin-top:5px;'>ICA Web Application Evaluation</div></div>");
    sb.Append("<div class='timestamp'><strong>Execution Date:</strong> " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</div>");
    sb.Append("</div>");

    sb.Append("<div class='summary-deck'>");
    sb.Append("<div class='card agent-card'><div class='card-title'>PCM Operator Agent</div><div class='card-value'>" + operatorUser + "</div></div>");
    sb.Append("<div class='card status-card'><div class='card-title'>Global Evaluation Status</div><div class='card-value' style='color:" + globalStatusColor + ";'>" + globalStatus + "</div></div>");
    sb.Append("<div class='card'><div class='card-title'>Consolidated Steps</div><div class='card-value'>" + passedSteps + " / " + totalSteps + " Steps Passed</div></div>");
    sb.Append("</div>");

    sb.Append("<table>");
    sb.Append("<tr><th style='width:15%;'>#</th><th style='width:35%;'>Test Step Context</th><th style='width:50%;'>Execution Details / Logs</th></tr>");

    for (int i = 0; i < steps.Count; i++)
    {
        var step = steps[i];
        bool isFailed = step.Status == "FAILED";
        string badgeStyle = isFailed ? "badge-danger" : "badge-success";

        sb.Append("<tr>");
        sb.Append("<td>" + (i + 1) + "</td>");
        sb.Append("<td><strong>" + System.Net.WebUtility.HtmlEncode(step.Name) + "</strong></td>");

        if (isFailed)
        {
            sb.Append("<td>");
            sb.Append("<span class='badge " + badgeStyle + "'>ERROR</span>");
            sb.Append("<pre>" + System.Net.WebUtility.HtmlEncode(step.Details) + "</pre>");
            if (!string.IsNullOrEmpty(step.ScreenshotPath))
            {
                sb.Append("<br><a class='screenshot-link' href='" + step.ScreenshotPath + "' target='_blank'>👁 View Error Screenshot</a>");
            }
            sb.Append("</td>");
        }
        else
        {
            sb.Append("<td><span class='badge " + badgeStyle + "' style='margin-right:10px;'>PASSED</span><span style='color: #CBD5E0;'>" + System.Net.WebUtility.HtmlEncode(step.Details) + "</span></td>");
        }
        sb.Append("</tr>");
    }

    sb.Append("</table></body></html>");
    File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
}

// --- CORE ENCAPSULATED SELENIUM FUNCTIONALITIES ---
void Login(EdgeDriver driver, Dictionary<string, string> config)
{
    WaitForElement(driver, By.XPath(config["usrNameTxtBox"])).SendKeys(config["usrName"]);
    WaitForElement(driver, By.XPath(config["usrPassTxtBox"])).SendKeys(config["usrPass"]);
    WaitForElement(driver, By.XPath(config["loginBtn"])).Click();
}

void ReportCase(EdgeDriver driver, Dictionary<string, string> config)
{
    WaitForElement(driver, By.XPath("//a[contains(text(),'Report')]")).Click();
    IWebElement dutyType = WaitForElement(driver, By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[8]/td/select"));
    var dutyTypeBox = new SelectElement(dutyType);
    dutyTypeBox.SelectByValue(config["dutyType"]);

    IWebElement shipmentNbrBox = WaitForElement(driver, By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[11]/td/input"));
    shipmentNbrBox.SendKeys(config["caShipmentNbr"]);

    IWebElement shipDateStart = WaitForElement(driver, By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[20]/td/span/input[1]"));
    shipDateStart.SendKeys(config["shipStartDate"]);

    IWebElement shipDateEnd = WaitForElement(driver, By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[23]/td/span/input[1]"));
    shipDateEnd.SendKeys(config["shipEndDate"]);

    IWebElement findBtn = WaitForElement(driver, By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[35]/td/input[1]"));
    findBtn.Click();

    IWebElement shipmentNbrResult = WaitForElement(driver, By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[3]/td/table[2]/tbody/tr[2]/td[2]"));
}

void UserAdmin(EdgeDriver driver, Dictionary<string, string> config)
{
    WaitForElement(driver, By.XPath("//a[contains(text(),'Administration')]")).Click();
}

void ManageAccount(EdgeDriver driver, Dictionary<string, string> config)
{
    WaitForElement(driver, By.XPath("//a[contains(text(),'Manage Account')]")).Click();
}

void UpdateFirmInfo(EdgeDriver driver, Dictionary<string, string> config)
{
    WaitForElement(driver, By.XPath("//a[contains(text(),'Update Firm Information')]")).Click();
}

void ReportHighValueShipment(EdgeDriver driver, Dictionary<string, string> config)
{
    WaitForElement(driver, By.XPath("//a[contains(text(),'Report')]")).Click();
    IWebElement dutyType = WaitForElement(driver, By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[8]/td/select"));
    var dutyTypeBox = new SelectElement(dutyType);
    dutyTypeBox.SelectByValue(config["dutyTypeHV"]);

    IWebElement shipmentNbrBox = WaitForElement(driver, By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[11]/td/input"));
    shipmentNbrBox.SendKeys(config["caShipmentNbr01"]);

    IWebElement shipDateStart = WaitForElement(driver, By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[20]/td/span/input[1]"));
    shipDateStart.SendKeys(config["shipStartDate"]);

    IWebElement shipDateEnd = WaitForElement(driver, By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[23]/td/span/input[1]"));
    shipDateEnd.SendKeys(config["shipEndDate"]);

    IWebElement findBtn = WaitForElement(driver, By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[35]/td/input[1]"));
    findBtn.Click();

    IWebElement shipmentNbrResult = WaitForElement(driver, By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[3]/td/table[2]/tbody/tr[2]/td[2]"));
}