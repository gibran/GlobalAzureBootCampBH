using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Luis;
using Microsoft.Bot.Builder.Luis.Models;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Sample.BootCamp.Actions;

namespace Microsoft.Bot.Sample.BootCamp.Dialogs
{
    // For more information about this template visit http://aka.ms/azurebots-csharp-luis
    [Serializable]
    public class BasicLuisDialog : LuisDialog<object>
    {
        public BasicLuisDialog() : base(new LuisService(new LuisModelAttribute(
            ConfigurationManager.AppSettings["LuisAppId"],
            ConfigurationManager.AppSettings["LuisAPIKey"],
            domain: ConfigurationManager.AppSettings["LuisAPIHostName"])))
        {
        }

        [LuisIntent("None")]
        public async Task NoneIntent(IDialogContext context, LuisResult result)
        {
            await this.ShowLuisResult(context, result);
        }

        [LuisIntent("Saudacao")]
        public async Task SaudacaoIntent(IDialogContext context, LuisResult result)
        {
            const string MESSAGE = @"Olá tudo bem? como posso te ajudar?";

            await context.PostAsync(MESSAGE);
            context.Wait(MessageReceived);
        }

        [LuisIntent("Acao")]
        public async Task AcaoIntent(IDialogContext context, LuisResult result)
        {
            var subscription = ObterSubscription(context);

            if (string.IsNullOrEmpty(subscription))
                return;

            var applicationId = ObterApplicationId(context);
            if (string.IsNullOrEmpty(applicationId))
                return;

            var secretKey = ObterSecretKey(context);
            if (string.IsNullOrEmpty(secretKey))
                return;

            var activity = context.Activity;
            var stateClient = activity.GetStateClient();
            var userData = stateClient.BotState.GetUserData(activity.ChannelId, activity.From.Id);

            ProcessadorResult processadorResult = null;

            if (result.Intents.Any(i => i.Intent.ToUpper() == "ACAO"))
            {
                if (result.Entities.Any(v => v.Type.ToUpper() == "LISTAR"))
                    processadorResult = await ProcessarListagem(result, userData);

                if (result.Entities.Any(i => i.Type.ToUpper() == "OPERACOES" && i.Entity.ToUpper() != "CRIAR"))
                    processadorResult = await ProcessarAcao(result, userData);

                if (result.Entities.Any(i => i.Type.ToUpper() == "OPERACOES" && i.Entity.ToUpper() == "CRIAR"))
                {
                    var resourceGroups = await ActionBuilder.ListResourceGroups(subscription, applicationId, secretKey);

                    var dialog = new PromptDialog.PromptChoice<string>(resourceGroups.Select(r => r.Name), "Em qual resource group deseja criar?", "Desculpa opção inválida", 3);
                    context.Call(dialog, ResourceGroupPromptResultAsync);
                    return;
                }
            }

            await stateClient.BotState.SetUserDataAsync(activity.ChannelId, activity.From.Id, userData);
            await context.PostAsync(processadorResult?.Mensagem);
            context.Wait(MessageReceived);
        }

        [LuisIntent("Ajuda")]
        public async Task AjudaIntent(IDialogContext context, LuisResult result)
        {

            const string MESSAGE = "Eu estou preparado para ajudar com as seguintes operações:" +
                                   "\n\r* Listar recursos" +
                                   "\n\r* Iniciar/Parar um recurso" +
                                   "\n\r* Criar ambiente website" +
                                   "\n\r* Limpar memória (subscription...)";

            await context.PostAsync(MESSAGE);
            context.Wait(MessageReceived);
        }

        [LuisIntent("Limpar")]
        public async Task LimparIntent(IDialogContext context, LuisResult result)
        {
            var activity = context.Activity;
            var stateClient = activity.GetStateClient();
            var userData = stateClient.BotState.GetUserData(activity.ChannelId, activity.From.Id);

            userData.RemoveProperty("SUBSCRIPTION");
            userData.RemoveProperty("APPLICATIONID");
            userData.RemoveProperty("SECRETKEY");

            await stateClient.BotState.SetUserDataAsync(activity.ChannelId, activity.From.Id, userData);

            await context.PostAsync("Memoria Limpa");
            context.Wait(MessageReceived);
        }


        private static async Task<ProcessadorResult> ProcessarAcao(LuisResult result, BotData userData)
        {
            var subscriptionId = userData.GetProperty<string>("SUBSCRIPTION");
            var applicationId = userData.GetProperty<string>("APPLICATIONID");
            var secretKey = userData.GetProperty<string>("SECRETKEY");

            var recursos = userData.GetProperty<List<AzureResource>>("RESOURCES");


            var acao = result.Entities.FirstOrDefault(a => string.Equals(a.Type.Trim(), "OPERACOES", StringComparison.CurrentCultureIgnoreCase));

            if (acao == null)
                return new ProcessadorResult
                {
                    Processado = false,
                    Mensagem = $"Acao informada não permitida."
                };

            var recurso = result.Entities.FirstOrDefault(a => a.Type.ToUpper() == "ACAORECURSO");

            if (recurso == null)
                return new ProcessadorResult
                {
                    Processado = false,
                    Mensagem = $"Necessário informar o nome do recuros que deseja {acao.Entity}"
                };

            var nomeRecurso = recurso.Entity.Replace(acao.Entity, "").Trim().Split(' ')[0].RemoverPontuacao();

            var targertResource = recursos.FirstOrDefault(r => string.Equals(r.Name.Trim(), nomeRecurso.Trim(), StringComparison.CurrentCultureIgnoreCase));
            if (targertResource == null)
                return new ProcessadorResult
                {
                    Processado = false,
                    Mensagem = $"Recuros informado não encontrado na lista de recursos listados."
                };


            var actionResult = "...";

            if (targertResource.Type == "sites")
            {
                var actionName = acao.Entity == "parar" ? "stop" : "start";
                actionResult = acao.Entity == "parar" ? "parando" : "iniciando";
                await ActionBuilder.StartStopResource(subscriptionId, applicationId, secretKey, targertResource.ResrouceGroupName, targertResource.Name, actionName);
            }
            else
            {
                var actionName = acao.Entity == "parar" ? "deallocate" : "start";
                actionResult = acao.Entity == "parar" ? "parando" : "iniciando";
                await ActionBuilder.StartDeallocateVm(subscriptionId, applicationId, secretKey, targertResource.ResrouceGroupName, targertResource.Name, actionName);
            }

            return new ProcessadorResult
            {
                Processado = true,
                Mensagem = $"O recurso solicitado está sendo {actionResult}"
            };
        }

        private static async Task<ProcessadorResult> ProcessarListagem(LuisResult result, BotData userData)
        {
            List<IAzureItem> resources;

            var subscriptionId = userData.GetProperty<string>("SUBSCRIPTION");
            var applicationId = userData.GetProperty<string>("APPLICATIONID");
            var secretKey = userData.GetProperty<string>("SECRETKEY");


            if (result.Entities.Any(v => v.Type.ToUpper() == "TODOSRECURSOS"))
            {
                resources = await ActionBuilder.ListResources(subscriptionId, applicationId, secretKey);
                userData.SetProperty("RESOURCES", resources);
            }
            else
            {
                resources = await ActionBuilder.ListResourceGroups(subscriptionId, applicationId, secretKey);
                userData.SetProperty("RESOURCEGROUPS", resources);
            }

            var mensagemResult = new StringBuilder();
            if (resources.Any())
                mensagemResult.AppendLine(resources[0].GetType().Name);

            foreach (var azureResource in resources)
                mensagemResult.AppendLine($"* {azureResource}");

            return new ProcessadorResult
            {
                Processado = true,
                Mensagem = mensagemResult.ToString()
            };
        }


        #region [ Criar Ambiente ]

        private async Task ResourceGroupPromptResultAsync(IDialogContext context, IAwaitable<string> result)
        {
            var activity = context.Activity;
            var stateClient = activity.GetStateClient();
            var userData = stateClient.BotState.GetUserData(activity.ChannelId, activity.From.Id);

            var targetResourceGroup = await result;

            userData.SetProperty("ACAO:CRIAR_AMBIENTE:RESOURCE_GROUP", targetResourceGroup);
            await stateClient.BotState.SetUserDataAsync(activity.ChannelId, activity.From.Id, userData);

            var dialog = new PromptDialog.PromptString("Qual o nome da aplicação?", "Nome do ambiente inválido.", 3);
            context.Call(dialog, CriarAmbientePromptResultAsync);
        }

        private async Task CriarAmbientePromptResultAsync(IDialogContext context, IAwaitable<string> result)
        {
            var webAppNameTarget = await result;

            var activity = context.Activity;
            var stateClient = activity.GetStateClient();
            var userData = stateClient.BotState.GetUserData(activity.ChannelId, activity.From.Id);

            var subscriptionId = userData.GetProperty<string>("SUBSCRIPTION");
            var applicationId = userData.GetProperty<string>("APPLICATIONID");
            var secretKey = userData.GetProperty<string>("SECRETKEY");

            var resourceGroupTarget = userData.GetProperty<string>("ACAO:CRIAR_AMBIENTE:RESOURCE_GROUP");

            await ActionBuilder.CreateWebApp(subscriptionId, applicationId, secretKey, resourceGroupTarget, webAppNameTarget);

            await context.PostAsync($"Ambiente criado: https://{webAppNameTarget}.azurewebsites.net");
            context.Wait(MessageReceived);
        }

        #endregion


        #region [ Obter configurações ]

        private string ObterSubscription(IDialogContext context)
        {
            var activity = context.Activity;
            var stateClient = activity.GetStateClient();
            var userData = stateClient.BotState.GetUserData(activity.ChannelId, activity.From.Id);

            var subscriptionTarget = userData.GetProperty<string>("SUBSCRIPTION");

            if (!string.IsNullOrEmpty(subscriptionTarget))
                return subscriptionTarget;

            var dialog = new PromptDialog.PromptString("**Informe a subscription**", "Subscription inválido.", 3);
            context.Call(dialog, ObterSubscriptionPromptResultAsync);

            return null;
        }

        private async Task ObterSubscriptionPromptResultAsync(IDialogContext context, IAwaitable<string> result)
        {
            var activity = context.Activity;
            var stateClient = activity.GetStateClient();
            var userData = stateClient.BotState.GetUserData(activity.ChannelId, activity.From.Id);

            var targetSubscription = await result;

            userData.SetProperty("SUBSCRIPTION", targetSubscription);
            await stateClient.BotState.SetUserDataAsync(activity.ChannelId, activity.From.Id, userData);

            await context.PostAsync("*Subscription selecionada.*");

            var applicationId = userData.GetProperty<string>("APPLICATIONID");
            var securityKey = userData.GetProperty<string>("SECURITYKEY");

            if (string.IsNullOrEmpty(applicationId))
            {
                var dialog = new PromptDialog.PromptString("**Informe a applicationId**", "ApplicationId inválido.", 3);
                context.Call(dialog, ObterApplicationIdPromptResultAsync);
            }
            else if (string.IsNullOrEmpty(securityKey))
            {
                var dialog = new PromptDialog.PromptString("**Informe a secretKey**", "SecretKey inválido.", 3);
                context.Call(dialog, ObterSecretKeyPromptResultAsync);
            }
            else
                context.Wait(MessageReceived);
        }

        private string ObterApplicationId(IDialogContext context)
        {
            var activity = context.Activity;
            var stateClient = activity.GetStateClient();
            var userData = stateClient.BotState.GetUserData(activity.ChannelId, activity.From.Id);

            var applicationIdTarget = userData.GetProperty<string>("APPLICATIONID");

            if (!string.IsNullOrEmpty(applicationIdTarget))
                return applicationIdTarget;

            var dialog = new PromptDialog.PromptString("Informe a applicationId", "ApplicationId inválido.", 3);
            context.Call(dialog, ObterApplicationIdPromptResultAsync);

            return null;
        }

        private async Task ObterApplicationIdPromptResultAsync(IDialogContext context, IAwaitable<string> result)
        {
            var activity = context.Activity;
            var stateClient = activity.GetStateClient();
            var userData = stateClient.BotState.GetUserData(activity.ChannelId, activity.From.Id);

            var targetSubscription = await result;

            userData.SetProperty("APPLICATIONID", targetSubscription);
            await stateClient.BotState.SetUserDataAsync(activity.ChannelId, activity.From.Id, userData);

            await context.PostAsync("*ApplicationId selecionada.*");

            var securityKey = userData.GetProperty<string>("SECURITYKEY");
            if (string.IsNullOrEmpty(securityKey))
            {
                var dialog = new PromptDialog.PromptString("**Informe a secretKey**", "SecretKey inválido.", 3);
                context.Call(dialog, ObterSecretKeyPromptResultAsync);
            }
            else
                context.Wait(MessageReceived);
        }

        private string ObterSecretKey(IDialogContext context)
        {
            var activity = context.Activity;
            var stateClient = activity.GetStateClient();
            var userData = stateClient.BotState.GetUserData(activity.ChannelId, activity.From.Id);

            var secretKeyTarget = userData.GetProperty<string>("SECRETKEY");

            if (!string.IsNullOrEmpty(secretKeyTarget))
                return secretKeyTarget;

            var dialog = new PromptDialog.PromptString("Informe a secretKey", "SecretKey inválido.", 3);
            context.Call(dialog, ObterSecretKeyPromptResultAsync);

            return null;
        }

        private async Task ObterSecretKeyPromptResultAsync(IDialogContext context, IAwaitable<string> result)
        {
            var activity = context.Activity;
            var stateClient = activity.GetStateClient();
            var userData = stateClient.BotState.GetUserData(activity.ChannelId, activity.From.Id);

            var targetSubscription = await result;

            userData.SetProperty("SECRETKEY", targetSubscription);
            await stateClient.BotState.SetUserDataAsync(activity.ChannelId, activity.From.Id, userData);

            await context.PostAsync("*SecretKey selecionada.*");
            context.Wait(MessageReceived);
        }


        #endregion

        private async Task ShowLuisResult(IDialogContext context, LuisResult result)
        {
            await context.PostAsync($"You have reached {result.Intents[0].Intent}. You said: {result.Query}");
            context.Wait(MessageReceived);
        }
    }
}