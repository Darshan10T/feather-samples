﻿using System;
using Telerik.Sitefinity.Events.Model;

namespace KendoEvents.Mvc.Models
{
    /// <summary>
    /// This is the view model used for displaying calendars.
    /// </summary>
    public class CalendarViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CalendarViewModel"/> class.
        /// </summary>
        /// <param name="calendar">The calendar.</param>
        public CalendarViewModel(Calendar calendar) 
        {
            this.CalendarId = calendar.Id;
            this.Color = calendar.Color;
        }

        /// <summary>
        /// Gets or sets the calendar identifier.
        /// </summary>
        /// <value>
        /// The calendar identifier.
        /// </value>
        public Guid CalendarId { get; set; }

        /// <summary>
        /// Gets or sets the color.
        /// </summary>
        /// <value>
        /// The color.
        /// </value>
        public string Color { get; set; }
    }
}
