using System.Collections.Generic;
namespace MovFileIntegrityChecker.Web.Services
{
    public class LocalizationService
    {
        private readonly Dictionary<string, string> _frenchTranslations = new()
        {
            // Scanner Dashboard
            { "ScannerDashboard", "Tableau de Bord" },
            { "ScanPath", "Chemin d'Analyse" },
            { "Options", "Options" },
            { "Recursive", "Récursif" },
            { "StartScan", "Démarrer l'Analyse" },
            { "StopScan", "Arrêter l'Analyse" },
            { "NextScanIn", "Prochaine analyse dans :" },
            { "AutomaticMode", "Mode Automatique" },
            { "Every", "Toutes les" },
            { "Hours", "heures" },
            { "NextScan", "Prochaine analyse :" },
            { "ConsoleOutput", "Sortie Console" },
            { "Clear", "Effacer" },
            { "Results", "Résultats" },
            { "Total", "Total :" },
            { "Valid", "Valides :" },
            { "Corrupted", "Corrompus :" },
            { "All", "Tous" },
            { "File", "Fichier" },
            { "Size", "Taille" },
            { "Status", "État" },
            { "Validation", "Validation" },
            { "Actions", "Actions" },
            { "Report", "Rapport" },
            { "Folder", "Dossier" },
            { "NoResultsFilter", "Aucun résultat ne correspond au filtre actuel." },
            { "NoResultsYet", "Aucun résultat pour le moment. Démarrez une analyse pour voir les données." },
            // Status messages
            { "Ready", "Prêt" },
            { "Scanning", "Analyse en cours..." },
            { "Completed", "Terminé" },
            { "Error", "Erreur" },
            { "Cancelled", "Annulé" },
            { "Stopping", "Arrêt en cours..." },
            { "SelectScanDirectory", "Sélectionner le Répertoire d'Analyse" },
            // Error Page
            { "ErrorTitle", "Erreur" },
            { "ErrorMessage", "Une erreur s'est produite lors du traitement de votre demande." },
            { "RequestId", "ID de Demande :" },
            { "DevelopmentMode", "Mode Développement" },
            { "DevelopmentModeDescription1", "Le passage à l'environnement de développement affichera des informations plus détaillées sur l'erreur qui s'est produite." },
            { "DevelopmentModeDescription2", "L'environnement de développement ne doit pas être activé pour les applications déployées. Cela peut entraîner l'affichage d'informations sensibles provenant d'exceptions aux utilisateurs finaux. Pour le débogage local, activez l'environnement de développement en définissant la variable d'environnement ASPNETCORE_ENVIRONMENT sur Development et en redémarrant l'application." },
            // Layout
            { "UnhandledError", "Une erreur non gérée s'est produite." },
            { "Reload", "Recharger" }
        };
        public string this[string key]
        {
            get
            {
                // Always return French translation
                return _frenchTranslations.TryGetValue(key, out var value) ? value : key;
            }
        }
    }
}
