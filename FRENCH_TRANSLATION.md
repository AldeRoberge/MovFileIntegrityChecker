# French Translation for Scanner Web UI
The scanner web UI has been successfully translated to French.
## Features Translated
- **Scanner Dashboard**: All UI elements including:
  - Scan path input and controls
  - Start/Stop scan buttons
  - Automatic mode settings
  - Console output
  - Results table
  - Filter options
  - Status messages
- **Error Page**: Complete translation of error messages and development mode information
- **Layout**: Error UI messages
## Language Configuration
The application is currently configured with:
- **Default Language**: French (fr)
- **Supported Languages**: English (en), French (fr)
### Changing the Default Language
To change the default language, edit Program.cs:
`csharp
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"), // Change "fr" to "en" for English
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});
`
## Resource Files
- Resources/Strings.resx - English (default) translations
- Resources/Strings.fr.resx - French translations
- Resources/Strings.cs - Resource class for localization
## Translation Coverage
All user-facing strings in the web UI have been translated, including:
- Page titles
- Form labels and buttons
- Status messages (Ready, Scanning, Completed, Error, Cancelled, Stopping)
- Table headers
- Filter options
- Console and results section headers
- Error messages and development mode information
## Testing the Translation
1. The app is currently set to French as the default language
2. Start the web application
3. You should see all UI elements in French
4. To test English, change the default culture in Program.cs and restart the app
