﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using System.Web.UI;
using System.Web.UI.WebControls;
using Telerik.Sitefinity.Forms.Model;
using Telerik.Sitefinity.Frontend.ContentBlock.Mvc.Controllers;
using Telerik.Sitefinity.Frontend.Forms.Mvc.Controllers;
using Telerik.Sitefinity.Frontend.Forms.Mvc.Controllers.Base;
using Telerik.Sitefinity.Frontend.Forms.Mvc.Models.Fields;
using Telerik.Sitefinity.Frontend.GridSystem;
using Telerik.Sitefinity.Model.Localization;
using Telerik.Sitefinity.Modules.Forms;
using Telerik.Sitefinity.Modules.Forms.Web.UI.Fields;
using Telerik.Sitefinity.Mvc.Proxy;
using Telerik.Sitefinity.Pages.Model;
using Telerik.Sitefinity.Security;
using Telerik.Sitefinity.Security.Model;
using Telerik.Sitefinity.Services;
using Telerik.Sitefinity.Utilities.TypeConverters;
using Telerik.Sitefinity.Web.UI.Fields;

namespace SitefinityWebApp
{
    public partial class MigrateForms : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            this.MigrateButton.Click += this.MigrateButton_Click;
        }

        private void MigrateButton_Click(object sender, EventArgs e)
        {
            this.Migrate();
            this.Label1.Text = "Done migrating!";
        }

        private void Migrate()
        {
            var formsManager = FormsManager.GetManager();
            var formDescriptions = formsManager.GetForms().Where(f => f.Framework != FormFramework.Mvc).ToArray();

            foreach (var formDescription in formDescriptions)
            {
                var formName = formDescription.Name + "_MVC";

                var duplicateForm = this.Duplicate(formDescription, formName, formsManager);
                duplicateForm.Framework = FormFramework.Mvc;
                duplicateForm.Title = formDescription.Title + "_MVC";

                this.TransferResponses(formDescription, duplicateForm, formsManager);
            }

            formsManager.SaveChanges();

            // Restart to apply the meta types changes.
            SystemManager.RestartApplication(true);
        }

        private FormDescription Duplicate(FormDescription formDescription, string formName, FormsManager manager)
        {
            var duplicateForm = manager.CreateForm(formName);
            var thisFormMaster = manager.Lifecycle.GetMaster(formDescription);

            // Form has been unpublished
            if (thisFormMaster == null)
            {
                this.CopyFormCommonData(formDescription, duplicateForm, manager);

                // Get permissions from ParentForm, because FormDraft is no ISecuredObject
                duplicateForm.CopySecurityFrom((ISecuredObject)formDescription, null, null);
            }
            else
            {
                this.CopyFormCommonData(thisFormMaster, duplicateForm, manager);

                // Get permissions from ParentForm, because FormDraft is no ISecuredObject
                duplicateForm.CopySecurityFrom((ISecuredObject)thisFormMaster.ParentForm, null, null);
            }

            return duplicateForm;
        }

        /// <summary>
        /// Copies the data from one IFormData object to another. Note: this method only copies common draft and non-draft
        /// data.
        /// </summary>
        /// <param name="formFrom">The source object.</param>
        /// <param name="formTo">The target object.</param>
        private void CopyFormCommonData<TControlA, TControlB>(IFormData<TControlA> formFrom, IFormData<TControlB> formTo, FormsManager manager)
            where TControlA : ControlData
            where TControlB : ControlData
        {
            formTo.LastControlId = formFrom.LastControlId;
            formTo.CssClass = formFrom.CssClass;
            formTo.FormLabelPlacement = formFrom.FormLabelPlacement;
            formTo.RedirectPageUrl = formFrom.RedirectPageUrl;
            formTo.SubmitAction = formFrom.SubmitAction;
            formTo.SubmitRestriction = formFrom.SubmitRestriction;

            formTo.SubmitActionAfterUpdate = formFrom.SubmitActionAfterUpdate;
            formTo.RedirectPageUrlAfterUpdate = formFrom.RedirectPageUrlAfterUpdate;

            LocalizationHelper.CopyLstring(formFrom.SuccessMessage, formTo.SuccessMessage);
            LocalizationHelper.CopyLstring(formFrom.SuccessMessageAfterFormUpdate, formTo.SuccessMessageAfterFormUpdate);

            this.CopyControls(formFrom.Controls, formTo.Controls, manager);

            manager.CopyPresentation(formFrom.Presentation, formTo.Presentation);
        }

        private void CopyControls<SrcT, TrgT>(IEnumerable<SrcT> source, IList<TrgT> target, FormsManager manager)
            where SrcT : ControlData
            where TrgT : ControlData
        {
            var traverser = new FormControlTraverser<SrcT, TrgT>(source, target, this.CopyControl<SrcT, TrgT>, manager);
            traverser.CopyControls("Body", "Body");
        }

        private TrgT CopyControl<SrcT, TrgT>(SrcT sourceControl, FormsManager manager)
            where SrcT : ControlData
            where TrgT : ControlData
        {
            TrgT migratedControlData;
            if (!sourceControl.IsLayoutControl)
            {
                var migratedControl = this.ConfigureFormControl(sourceControl, manager);

                // Placeholder is updated later.
                migratedControlData = manager.CreateControl<TrgT>(migratedControl, "Body");
            }
            else
            {
                migratedControlData = manager.CreateControl<TrgT>();
                manager.CopyControl(sourceControl, migratedControlData);

                this.ConfigureLayoutControl<TrgT>(migratedControlData);
            }

            migratedControlData.Caption = sourceControl.Caption;

            return migratedControlData;
        }

        private void ConfigureLayoutControl<TrgT>(TrgT migratedControlData)
            where TrgT : ControlData
        {
            var layoutProperty = migratedControlData.Properties.FirstOrDefault(c => c.Name == "Layout");
            if (layoutProperty == null)
                return;

            if (layoutProperty.Value != null && layoutProperty.Value.StartsWith("~/") && this.layoutMap.ContainsKey(layoutProperty.Value))
            {
                migratedControlData.ObjectType = typeof(GridControl).FullName;
                layoutProperty.Value = this.layoutMap[layoutProperty.Value];
            }
        }

        private Control ConfigureFormControl(ControlData formControlData, FormsManager manager)
        {
            Control control = manager.LoadControl(formControlData, null);

            var controlType = control.GetType();

            ElementConfiguration elementConfiguration = null;
            foreach (var pair in this.fieldMap)
            {
                if (pair.Key.IsAssignableFrom(controlType))
                {
                    elementConfiguration = pair.Value;
                    if (pair.Value == null)
                        return null;

                    break;
                }
            }

            if (elementConfiguration == null)
                elementConfiguration = this.fieldMap[typeof(FormTextBox)];

            var mvcProxy = new MvcControllerProxy();
            mvcProxy.ControllerName = elementConfiguration.BackendFieldType.FullName;

            var newController = Activator.CreateInstance(elementConfiguration.BackendFieldType);

            var formField = control as IFormFieldControl;

            if (newController is IFormFieldController<IFormFieldModel> && formField != null)
            {
                var fieldController = (IFormFieldController<IFormFieldModel>)newController;
                var fieldControl = formField as FieldControl;
                if (fieldControl != null)
                {
                    fieldController.MetaField = formField.MetaField;
                    fieldController.MetaField.Title = fieldControl.Title;
                    fieldController.Model.CssClass = fieldControl.CssClass;
                    fieldController.Model.ValidatorDefinition = fieldControl.ValidatorDefinition;
                }
            }
            else if (newController is IFormElementController<IFormElementModel>)
            {
                var elementController = (IFormElementController<IFormElementModel>)newController;
                elementController.Model.CssClass = ((WebControl)control).CssClass;
            }

            if (elementConfiguration.ElementConfigurator != null)
            {
                elementConfiguration.ElementConfigurator.Configure(control, (Controller)newController);
            }

            mvcProxy.Settings = new ControllerSettings((Controller)newController);

            return (Control)mvcProxy;
        }

        private void TransferResponses(FormDescription originalForm, FormDescription duplicateForm, FormsManager manager)
        {
            // If the original form does not have a type for its entries then there is nothing to transfer.
            if (manager.GetMetadataManager().GetMetaType(manager.Provider.FormsNamespace, originalForm.Name) == null)
                return;

            // Create live versions of the controls that have a true Published property. Still the form should not be published.
            var draft = manager.Lifecycle.Edit(duplicateForm);
            manager.Lifecycle.Publish(draft);
            manager.Lifecycle.Unpublish(duplicateForm);

            // If a type was create while publishing we should delete it. We will use the type of the original form.
            var metaType = manager.GetMetadataManager().GetMetaType(manager.Provider.FormsNamespace, duplicateForm.Name);
            if (metaType != null)
            {
                manager.GetMetadataManager().Delete(metaType);
            }

            // Take the original form name. As a result the new form seizes the original meta type and all its items (the responses).
            duplicateForm.Name = originalForm.Name;

            if (!manager.GetForms().Any(f => f.Name == originalForm.Name + "_legacy"))
                originalForm.Name = originalForm.Name + "_legacy";
            else
                originalForm.Name = originalForm.Name + Guid.NewGuid().ToString("N");
				
			duplicateForm.FormEntriesSeed = originalForm.FormEntriesSeed;

            // Create a new dynamic type for the old form.
            foreach (var control in originalForm.Controls)
                control.Published = false;

            manager.BuildDynamicType(originalForm);
        }

        private static readonly Type formFileUploadType = TypeResolutionService.ResolveType("Telerik.Sitefinity.Modules.Forms.Web.UI.Fields.FormFileUpload");

        /// <summary>
        /// Mapping of WebForms form fields to MVC form field controllers.
        /// </summary>
        private readonly Dictionary<Type, ElementConfiguration> fieldMap = new Dictionary<Type, ElementConfiguration>()
            {
                // Checkboxes
                { typeof(FormCheckboxes), new ElementConfiguration(typeof(CheckboxesFieldController), new CheckboxesFieldConfigurator()) },

                // Dropdown list
                { typeof(FormDropDownList), new ElementConfiguration(typeof(DropdownListFieldController), new DropdownFieldConfigurator()) },

                // Multiple choice
                { typeof(FormMultipleChoice), new ElementConfiguration(typeof(MultipleChoiceFieldController), new MultipleChoiceFieldConfigurator()) },

                // Paragraph text box
                { typeof(FormParagraphTextBox), new ElementConfiguration(typeof(ParagraphTextFieldController), new ParagraphFieldConfigurator()) },

                // Textbox
                { typeof(FormTextBox), new ElementConfiguration(typeof(TextFieldController), new TextFieldConfigurator()) },

                // File upload
                { MigrateForms.formFileUploadType, new ElementConfiguration(typeof(FileFieldController), new FileFieldConfigurator()) },

                // Submit button
                { typeof(FormSubmitButton), new ElementConfiguration(typeof(SubmitButtonController), new ButtonElementConfigurator()) },

                // Forms Captcha
                { typeof(FormCaptcha),  new ElementConfiguration(typeof(CaptchaController), new CaptchaConfigurator()) },

                // Section header
                { typeof(FormSectionHeader),  new ElementConfiguration(typeof(SectionHeaderController), new SectionElementConfigurator()) },

                // Instruction text
                { typeof(FormInstructionalText),  new ElementConfiguration(typeof(ContentBlockController), new ContentBlockConfigurator()) }
            };

        /// <summary>
        /// Mapping of layout control templates to corresponding grid widget templates.
        /// </summary>
        private readonly Dictionary<string, string> layoutMap = new Dictionary<string, string>()
            {
                // 100%
                { "~/SFRes/Telerik.Sitefinity.Resources.Templates.Layouts.Column1Template.ascx", "~/Frontend-Assembly/Telerik.Sitefinity.Frontend/GridSystem/Templates/grid-12.html" },

                // 25% + 75%
                { "~/SFRes/Telerik.Sitefinity.Resources.Templates.Layouts.Column2Template1.ascx", "~/Frontend-Assembly/Telerik.Sitefinity.Frontend/GridSystem/Templates/grid-3+9.html" },

                // 33% + 67%
                { "~/SFRes/Telerik.Sitefinity.Resources.Templates.Layouts.Column2Template2.ascx", "~/Frontend-Assembly/Telerik.Sitefinity.Frontend/GridSystem/Templates/grid-4+8.html" },

                // 50% + 50%
                { "~/SFRes/Telerik.Sitefinity.Resources.Templates.Layouts.Column2Template3.ascx", "~/Frontend-Assembly/Telerik.Sitefinity.Frontend/GridSystem/Templates/grid-6+6.html" },

                // 67% + 33%
                { "~/SFRes/Telerik.Sitefinity.Resources.Templates.Layouts.Column2Template4.ascx", "~/Frontend-Assembly/Telerik.Sitefinity.Frontend/GridSystem/Templates/grid-8+4.html" },

                // 75% + 25%
                { "~/SFRes/Telerik.Sitefinity.Resources.Templates.Layouts.Column2Template5.ascx", "~/Frontend-Assembly/Telerik.Sitefinity.Frontend/GridSystem/Templates/grid-9+3.html" },

                // 33% + 34% + 33%
                { "~/SFRes/Telerik.Sitefinity.Resources.Templates.Layouts.Column3Template1.ascx", "~/Frontend-Assembly/Telerik.Sitefinity.Frontend/GridSystem/Templates/grid-4+4+4.html" },

                // 25% + 50% + 25%
                { "~/SFRes/Telerik.Sitefinity.Resources.Templates.Layouts.Column3Template2.ascx", "~/Frontend-Assembly/Telerik.Sitefinity.Frontend/GridSystem/Templates/grid-3+6+3.html" },

                // 4 x 25%
                { "~/SFRes/Telerik.Sitefinity.Resources.Templates.Layouts.Column4Template1.ascx", "~/Frontend-Assembly/Telerik.Sitefinity.Frontend/GridSystem/Templates/grid-3+3+3+3.html" },

                // 5 x 20%
                { "~/SFRes/Telerik.Sitefinity.Resources.Templates.Layouts.Column5Template1.ascx", "~/Frontend-Assembly/Telerik.Sitefinity.Frontend/GridSystem/Templates/grid-2+3+2+3+2.html" }
            };

        private class FormControlTraverser<SrcT, TrgT>
            where SrcT : ControlData
            where TrgT : ControlData
        {
            public FormControlTraverser(IEnumerable<SrcT> source, IList<TrgT> target, Func<SrcT, FormsManager, TrgT> copyControlDelegate, FormsManager manager)
            {
                this._source = source;
                this._target = target;
                this._copyControlDelegate = copyControlDelegate;
                this._manager = manager;
            }

            public void CopyControls(string sourcePlaceholder, string targetPlaceholder)
            {
                var controlsToCopy = this._source.Where(c => c.PlaceHolder == sourcePlaceholder).ToDictionary(c => c.SiblingId);

                var currentSourceSiblingId = Guid.Empty;
                var currentTargetSiblingId = Guid.Empty;
                while (controlsToCopy.Count > 0)
                {
                    if (!controlsToCopy.ContainsKey(currentSourceSiblingId))
                        break;

                    var currentSourceControl = controlsToCopy[currentSourceSiblingId];
                    var newControl = this._copyControlDelegate(currentSourceControl, this._manager);
                    newControl.PlaceHolder = targetPlaceholder;
                    newControl.SiblingId = currentTargetSiblingId;

                    this._target.Add(newControl);
                    controlsToCopy.Remove(currentSourceSiblingId);

                    currentSourceSiblingId = currentSourceControl.Id;
                    currentTargetSiblingId = newControl.Id;

                    if (currentSourceControl.PlaceHolders != null && newControl.PlaceHolders != null)
                    {
                        for (var i = 0; i < currentSourceControl.PlaceHolders.Length; i++)
                        {
                            this.CopyControls(currentSourceControl.PlaceHolders[i], newControl.PlaceHolders[Math.Min(i, newControl.PlaceHolders.Length - 1)]);
                        }
                    }
                }
            }

            private readonly IEnumerable<SrcT> _source;
            private readonly IList<TrgT> _target;
            private readonly Func<SrcT, FormsManager, TrgT> _copyControlDelegate;
            private readonly FormsManager _manager;
        }
    }
}