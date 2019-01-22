import { Component, OnInit, ViewChild } from '@angular/core';
import { Requirement, Question } from '../../models/set-builder.model';
import { SetBuilderService } from '../../services/set-builder.service';
import { Router, ActivatedRoute } from '@angular/router';
import { AngularEditorConfig } from '@kolkov/angular-editor';
import { MatDialog } from '@angular/material';
import { ConfirmComponent } from '../../dialogs/confirm/confirm.component';


@Component({
  selector: 'app-requirement-detail',
  templateUrl: './requirement-detail.component.html',
  // tslint:disable-next-line:use-host-property-decorator
  host: { class: 'd-flex flex-column flex-11a w-100' }
})
export class RequirementDetailComponent implements OnInit {

  r: Requirement = {};
  rBackup: Requirement = {};

  titleEmpty = false;
  textEmpty = false;

  questionBeingEdited: Question = null;
  originalQuestionText: string = null;
  editedQuestionInUse = false;

  @ViewChild('editQ') editQControl;

  editorConfig: AngularEditorConfig = {
    editable: true,
    spellcheck: true,
    height: '25rem',
    minHeight: '5rem',
    placeholder: 'Enter text here...',
    translate: 'no',
    uploadUrl: 'v1/images', // if needed
    customClasses: [ // optional
      {
        name: "quote",
        class: "quote",
      },
      {
        name: 'redText',
        class: 'redText'
      },
      {
        name: "titleText",
        class: "titleText",
        tag: "h1",
      },
    ]
  };


  constructor(private setBuilderSvc: SetBuilderService,
    private route: ActivatedRoute,
    private dialog: MatDialog) { }

  ngOnInit() {
    let requirementID = 0;
    if (!!this.setBuilderSvc.activeRequirement) {
      requirementID = this.setBuilderSvc.activeRequirement.RequirementID;
    } else {
      // if the service doesn't know it, try to get it from the URI
      requirementID = this.route.snapshot.params['id'];
    }

    this.setBuilderSvc.getRequirement(requirementID).subscribe((result: Requirement) => {
      this.r = result;
      this.rBackup = this.r;
      this.setBuilderSvc.activeRequirement = this.r;
    });
  }

  formatLinebreaks(text: string) {
    return this.setBuilderSvc.formatLinebreaks(text);
  }

  /**
   *
   */
  updateRequirement(e: Event) {
    this.titleEmpty = (this.r.Title.trim().length === 0);
    this.textEmpty = (this.r.RequirementText.trim().length === 0);

    // Don't allow either of these fields to be blanked out.
    if (this.titleEmpty) {
      this.r.Title = this.rBackup.Title;
    }
    if (this.textEmpty) {
      this.r.RequirementText = this.rBackup.RequirementText;
    }
    if (this.titleEmpty || this.textEmpty) {
      return;
    }

    // call API
    this.setBuilderSvc.updateRequirement(this.r).subscribe();
  }

  /**
   * Not all 'blur' events are firing.  If focus is in the editor and you
   * click the cursor out onto the page, there's no blur event.
   * This needs to be figured out.
   */
  onBlur(e: Event) {
    console.log('onBlur: ');
    console.log(e);
    this.updateRequirement(e);
  }

  /**
  *
  */
  hasSAL(r: Requirement, level: string): boolean {
    if (!r) {
      return false;
    }
    return (!!r.SalLevels && r.SalLevels.indexOf(level) >= 0);
  }

  /**
   * Includes/removes the level from the list of applicable SAL levels for the question.
   */
  toggleSAL(r: Requirement, level: string, e) {
    let state = false;
    const checked = e.target.checked;
    const a = r.SalLevels.indexOf(level);

    if (checked) {
      if (a <= 0) {
        r.SalLevels.push(level);
        state = true;
      }
    } else if (a >= 0) {
      r.SalLevels = r.SalLevels.filter(x => x !== level);
      state = false;
    }

    this.setBuilderSvc.setSalLevel(r.RequirementID, 0, level, state).subscribe();
  }

  /**
   * Indicates if no SAL levels are currently selected for the question.
   */
  missingSAL(r: Requirement) {
    if (!r) {
      return false;
    }
    if (!r.SalLevels) {
      return true;
    }
    if (r.SalLevels.length === 0) {
      return true;
    }
    return false;
  }

  addQuestion() {
    // set the requirement as 'active' in the service
    this.setBuilderSvc.activeRequirement = this.r;
    // navigate to add-question
    this.setBuilderSvc.navAddQuestion();
  }

  removeQuestion(q: Question) {
    // confirm
    const dialogRef = this.dialog.open(ConfirmComponent);
    dialogRef.componentInstance.confirmMessage =
      "Are you sure you want to remove the question from the requirement?";

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.dropQuestion(q);
      }
    });
  }


  /**
   * Remove the question from the requirement
   */
  dropQuestion(q: Question) {
    console.log('requirement-detail removeQuestion: ' + q.QuestionID);
    this.setBuilderSvc.removeQuestion(q.QuestionID).subscribe(() => {
      console.log('back from removeQuestion!');

      // remove the deleted question from the collection
      const i = this.r.Questions.findIndex((x: any) => x.QuestionID === q.QuestionID);
      this.r.Questions.splice(i, 1);
    });
  }

  /**
   *
   */
  startQuestionEdit(q: Question) {
    this.questionBeingEdited = q;
    this.originalQuestionText = q.QuestionText;

    setTimeout(() => {
      this.editQControl.nativeElement.focus();
      this.editQControl.nativeElement.setSelectionRange(0, 0);
    }, 20);

    this.setBuilderSvc.isQuestionInUse(q).subscribe((inUse: boolean) => {
      this.editedQuestionInUse = inUse;
    });
  }

  /**
   *
   */
  endQuestionEdit(q: Question) {
    this.editedQuestionInUse = false;
    this.questionBeingEdited = null;
    this.setBuilderSvc.updateQuestionText(q).subscribe();
  }

  abandonQuestionEdit(q: Question) {
    q.QuestionText = this.originalQuestionText;
    this.editedQuestionInUse = false;
    this.questionBeingEdited = null;
  }
}
