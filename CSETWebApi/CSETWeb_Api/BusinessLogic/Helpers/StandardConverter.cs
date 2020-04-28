//////////////////////////////// 
// 
//   Copyright 2020 Battelle Energy Alliance, LLC  
// 
// 
//////////////////////////////// 
using BusinessLogic.Models;
using CSETWeb_Api.BusinessLogic.Models;
using DataLayerCore.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static BusinessLogic.Models.ExternalRequirement;

namespace CSETWeb_Api.BusinessLogic.Helpers
{
    public static class StandardConverter
    {

        public static async Task<ConverterResult<SETS>> ToSet(this IExternalStandard externalStandard)
        {
            return await externalStandard.ToSet(new ConsoleLogger());
        }
        public static async Task<ConverterResult<SETS>> ToSet(this IExternalStandard externalStandard, ILogger logger)
        {
            var questionDictionary = new Dictionary<string, NEW_QUESTION>();
            var requirementList = new List<string>();

            var categoryDictionary = new Dictionary<string, STANDARD_CATEGORY>();
            var result = new ConverterResult<SETS>(logger);
            SETS_CATEGORY category;
            int? categoryOrder = 0;
            var setname = Regex.Replace(externalStandard.ShortName, @"\W", "_");
            var db = new CSET_Context();
            try
            {
                var documentImporter = new DocumentImporter();
                var set = result.Result;

                var existingSet = db.SETS.FirstOrDefault(s => s.Set_Name == setname);
                if (existingSet != null)
                {
                    result.LogError("Module already exists.  If this is a new version, please change the ShortName field to reflect this.");
                }
                category = db.SETS_CATEGORY.FirstOrDefault(s => s.Set_Category_Name.Trim().ToLower() == externalStandard.Category.Trim().ToLower());

                if (category == null)
                {
                    result.LogError("Module Category is invalid.  Please check the spelling and try again.");
                }
                else
                {
                    categoryOrder = category.SETS.Max(s => s.Order_In_Category);
                }

                set.Set_Category_Id = category?.Set_Category_Id;
                set.Order_In_Category = categoryOrder;
                set.Short_Name = externalStandard.ShortName;
                set.Set_Name = setname;
                set.Full_Name = externalStandard.Name;
                set.Is_Custom = true;
                set.Is_Question = true;
                set.Is_Requirement = true;
                set.Is_Displayed = true;
                set.IsEncryptedModuleOpen = true;
                set.IsEncryptedModule = false;
                set.Is_Deprecated = false;

                set.Standard_ToolTip = externalStandard.Summary;


                set.NEW_REQUIREMENT = new List<NEW_REQUIREMENT>();
                var requirements = set.NEW_REQUIREMENT;
                int counter = 0;
                foreach (var requirement in externalStandard.Requirements)
                {
                    //skip duplicates
                    if (!requirementList.Any(s => s == requirement.Identifier.Trim().ToLower() + "|||" + requirement.Text.Trim().ToLower()))
                    {
                        counter++;
                        var requirementResult = await requirement.ToRequirement(set.Set_Name, new ConsoleLogger());
                        if (requirementResult.IsSuccess)
                        {
                            requirementResult.Result.REQUIREMENT_SETS.FirstOrDefault().Requirement_Sequence = counter;
                            if (requirementResult.Result.Standard_CategoryNavigation != null)
                            {
                                STANDARD_CATEGORY tempCategory;
                                if (categoryDictionary.TryGetValue(requirementResult.Result.Standard_CategoryNavigation.Standard_Category1, out tempCategory))
                                {
                                    requirementResult.Result.Standard_CategoryNavigation = tempCategory;
                                }
                                else
                                {
                                    categoryDictionary.Add(requirementResult.Result.Standard_CategoryNavigation.Standard_Category1, requirementResult.Result.Standard_CategoryNavigation);
                                }
                            }

                            foreach (var question in requirementResult.Result.NEW_QUESTIONs().ToList())
                            {
                                NEW_QUESTION existingQuestion;
                                if (questionDictionary.TryGetValue(question.Simple_Question, out existingQuestion))
                                {
                                    requirementResult.Result.REQUIREMENT_QUESTIONS.Remove(new REQUIREMENT_QUESTIONS() { Question_Id = question.Question_Id, Requirement_Id = requirementResult.Result.Requirement_Id });
                                }
                                else
                                {
                                    questionDictionary.Add(question.Simple_Question, question);
                                }
                            }
                            requirementList.Add(requirementResult.Result.Requirement_Title.Trim().ToLower() + "|||" + requirementResult.Result.Requirement_Text.Trim().ToLower());
                            requirements.Add(requirementResult.Result);
                        }
                        else
                        {
                            requirementResult.ErrorMessages.ToList().ForEach(s => result.LogError(s));
                        }
                    }
                }
                var questions = requirements.SelectMany(s => s.NEW_QUESTIONs()).ToList();
                for (var i = 1; i <= questions.Count(); i++)
                {
                    var question = questions[i - 1];
                    question.Std_Ref_Number = i;
                    question.Std_Ref_Id = question.Std_Ref + question.Std_Ref_Number;
                }
            }
            catch
            {
                result.LogError("Module could not be added.");
            }

            db.SaveChanges();

            return result;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="standard"></param>
        /// <returns></returns>
        public static ExternalStandard ToExternalStandard(this SETS standard)
        {
            var externalStandard = new ExternalStandard();
            externalStandard.ShortName = standard.Short_Name;
            externalStandard.Name = standard.Full_Name;
            externalStandard.Summary = standard.Standard_ToolTip;
            externalStandard.Category = standard.Set_Category_.Set_Category_Name;

            var requirements = new List<ExternalRequirement>();
            //Caching for performance
            using (var db = new CSET_Context())
            {
                //db.Configuration.ProxyCreationEnabled = false;
                //db.Configuration.AutoDetectChangesEnabled = false;
                //db.Configuration.LazyLoadingEnabled = false;

                var reqs = standard.NEW_REQUIREMENT.ToList();
                Dictionary<int, List<QuestionAndHeading>> reqQuestions = reqs.Select(s => new
                {
                    s.Requirement_Id,
                    Questions = s.NEW_QUESTIONs().Select(t =>
new QuestionAndHeading() { Simple_Question = t.Simple_Question, Heading_Pair_Id = t.Heading_Pair_Id })
                })
                    .ToDictionary(s => s.Requirement_Id, s => s.Questions.ToList());

                var reqHeadingIds = reqs.Select(s => s.Question_Group_Heading_Id).ToList();
                //var questionHeadings = from a in db.REQUIREMENT_QUESTIONS
                //                       join b in db.new on a.Question_Id equals b.Question_Id
                //                       join c in db.NEW_QUESTION_SETS on b.Question_Id equals c.Question_Id
                //                       where c.Set_Name == standard.Set_Name
                //                       select b.question_group_heading_id
                var questionHeadings = reqQuestions.SelectMany(s => s.Value.Select(t => t.Heading_Pair_Id)).Distinct().ToList();
                var reqHeadings = db.QUESTION_GROUP_HEADING.Where(s => reqHeadingIds.Contains(s.Question_Group_Heading_Id)).ToDictionary(s => s.Question_Group_Heading_Id, s => s.Question_Group_Heading1);
                var headingPairs = db.UNIVERSAL_SUB_CATEGORY_HEADINGS.Where(s => questionHeadings.Contains(s.Heading_Pair_Id));
                var subcategories = headingPairs.Join(db.UNIVERSAL_SUB_CATEGORIES, s => s.Universal_Sub_Category_Id, s => s.Universal_Sub_Category_Id, (s, t) => new { s.Heading_Pair_Id, category = t })
                              .ToDictionary(s => s.Heading_Pair_Id, s => s.category.Universal_Sub_Category);
                var headings = headingPairs.Join(db.QUESTION_GROUP_HEADING, s => s.Question_Group_Heading_Id, s => s.Question_Group_Heading_Id, (s, t) => new { s.Heading_Pair_Id, category = t })
                              .ToDictionary(s => s.Heading_Pair_Id, s => s.category.Question_Group_Heading1);

                var reqReferences = reqs.Select(s => new
                {
                    s.Requirement_Id,
                    Resources = s.REQUIREMENT_REFERENCES.Select(t =>
                      new ExternalResource
                      {
                          Destination = t.Destination_String,
                          FileName = t.Gen_File_.File_Name,
                          PageNumber = t.Page_Number,
                          SectionReference = t.Section_Ref
                      })
                }).ToDictionary(t => t.Requirement_Id, t => t.Resources);

                var reqSource = reqs.Select(s => new
                {
                    s.Requirement_Id,
                    Resource = s.REQUIREMENT_SOURCE_FILES.Select(t =>
                                      new ExternalResource
                                      {
                                          Destination = t.Destination_String,
                                          FileName = t.Gen_File_.File_Name,
                                          PageNumber = t.Page_Number,
                                          SectionReference = t.Section_Ref
                                      }).FirstOrDefault()
                }).ToDictionary(t => t.Requirement_Id, t => t.Resource);

                var reqLevels = new Dictionary<int, List<string>>();
                var tempLevels = reqs.Select(s => new { s.Requirement_Id, levels = s.REQUIREMENT_LEVELS.Select(t => t.Standard_Level) }).ToList();

                if (tempLevels.Any())
                {
                    reqLevels = tempLevels.ToDictionary(s => s.Requirement_Id, s => s.levels.ToList());
                }

                foreach (var requirement in reqs)
                {
                    var externalRequirement = new ExternalRequirement()
                    {
                        Identifier = requirement.Requirement_Title,
                        Text = requirement.Requirement_Text,
                        Category = requirement.Standard_Category,
                        Weight = requirement.Weight ?? 0,
                        Subcategory = requirement.Standard_Sub_Category,
                        Supplemental = requirement.Supplemental_Info
                    };
                    var headingPairId = reqQuestions[requirement.Requirement_Id].Select(s => s.Heading_Pair_Id).FirstOrDefault(s => s != 0);

                    // References
                    var references = externalRequirement.References;
                    reqReferences.TryGetValue(requirement.Requirement_Id, out references);
                    externalRequirement.References = references.ToList();

                    // Heading
                    string heading = null;
                    headings.TryGetValue(headingPairId, out heading);
                    if (String.IsNullOrEmpty(heading))
                    {
                        reqHeadings.TryGetValue(requirement.Question_Group_Heading_Id, out heading);
                    }
                    if (String.IsNullOrEmpty(heading))
                    {
                        throw new Exception("Heading is not valid");
                    }
                    externalRequirement.Heading = heading;

                    // Questions
                    List<QuestionAndHeading> questions = new List<QuestionAndHeading>();
                    reqQuestions.TryGetValue(requirement.Requirement_Id, out questions);
                    externalRequirement.Questions = new QuestionList();
                    foreach (QuestionAndHeading h in questions)
                        externalRequirement.Questions.Add(h.Simple_Question);

                    // Subheading
                    string subheading = null;
                    subcategories.TryGetValue(headingPairId, out subheading);
                    if (subheading == null)
                    {
                        subheading = heading;
                    }
                    externalRequirement.Subheading = subheading;

                    // Source
                    var source = externalRequirement.Source;
                    reqSource.TryGetValue(requirement.Requirement_Id, out source);
                    externalRequirement.Source = source;

                    // SAL
                    externalRequirement.SecurityAssuranceLevels = new List<string>();
                    foreach (var s in reqLevels[requirement.Requirement_Id])
                    {
                        externalRequirement.SecurityAssuranceLevels.Add(s);
                    }


                    requirements.Add(externalRequirement);
                }
                externalStandard.Requirements = requirements;
            }
            return externalStandard;
        }
    }


    /// <summary>
    /// 
    /// </summary>
    class QuestionAndHeading
    {
        public string Simple_Question { get; set; }
        public int Heading_Pair_Id { get; set; }
    }
}


